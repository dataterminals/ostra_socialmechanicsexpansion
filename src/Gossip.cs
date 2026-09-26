using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace SocialMechanicsExpansion
{
    /// <summary>
    /// Opinions travel. Relationship.StoreIAConds runs on whoever an exchange just affected
    /// (the listener) with the person who affected them (the speaker). Once per CooldownHours
    /// per pair, the speaker may bring up a third person (the subject).
    ///
    /// Who: someone the speaker knows well enough to have a real opinion of, weighted by how
    /// strongly they lean, and doubled for people the listener knows too.
    ///
    /// Whether: BaseChance × how hot the opinion is (lukewarm comes up half as often) ×
    /// talkativeness (Gregarious 1.5, Shy 0.5). Nothing at all without some trust.
    ///
    /// How much it lands, as a share of the speaker's feeling:
    ///
    ///   MaxShare × trust × (1 − experience) × credibility × exaggeration
    ///
    ///   trust        the listener's regard for the speaker, 0..1: how kind and how familiar their
    ///                relationship is, with floors for a friend (0.6), family (0.5), a crush (0.8);
    ///   experience   the listener's own familiarity with the subject, f / (f + 100): hearsay
    ///                barely moves someone who knows the subject well; strangers count as none;
    ///   credibility  Charismatic ×1.25; Honest ×1.25 and Liar ×0.5, but only once the listener has
    ///                learned that trait; an Observant listener ×0.8, an Obtuse one ×1.25;
    ///   exaggeration a Liar or Treacherous speaker passes on 1.5× what they really feel.
    ///
    /// Then the listener reacts:
    ///   pushed back  they know the subject firsthand (familiarity 50+) and lean clearly the
    ///                other way: they don't budge, and the same formula runs in reverse, softening
    ///                the speaker's view instead. A Loyal listener whose friend was run down also
    ///                loses a little respect for the speaker;
    ///   shrugged     what would land is under half a point of familiarity: nothing happens;
    ///   agreed       their tally on the subject moves by that share of the speaker's, capped at
    ///                Cap familiarity. Someone they never met is added as a stranger heard about.
    ///
    /// Labels still wait for a real meeting, so meeting someone whose friend hates you starts off
    /// frosty. Rumors are applied on the next frame, outside the game's exchange.
    /// </summary>
    public static class Gossip
    {
        private const double MinOpinion = 50.0;         // the speaker must know them at least as an acquaintance would
        private const double MinTrust = 0.05;
        private const double TrustFullAt = 100.0;       // familiarity at which trust stops growing with it
        private const double ExperienceScale = 100.0;   // own familiarity at which hearsay does half as much
        private const double ArgueFrom = 50.0;          // own familiarity needed to argue back
        private const double ArgueLean = 0.2;           // how clearly the listener must lean the other way
        private const double MinEffect = 0.5;           // a rumor moving familiarity by less than this is shrugged off
        private const double LoyalRespectCost = 2.0;
        private const int Remembered = 20;

        public sealed class Rumor
        {
            public double Epoch;
            public string Speaker, Listener, Subject;
            public bool Kindly;
            public string Reaction;     // agreed, shrugged, pushed back
            public double Moved;        // familiarity moved, on whichever side moved
        }

        private static readonly Queue<(CondOwner listener, CondOwner speaker, Relationship opinion)> Pending =
            new Queue<(CondOwner, CondOwner, Relationship)>();
        private static readonly Dictionary<string, double> LastRoll = new Dictionary<string, double>();
        private static readonly List<Rumor> Recent = new List<Rumor>();
        private static readonly Random Rng = new Random();

        /// <summary>The latest rumors passed this session, newest last. For tools such as OstraScope.</summary>
        public static IReadOnlyList<Rumor> RecentRumors => Recent;

        internal static void Heard(CondOwner listener, Relationship toSpeaker, CondOwner speaker)
        {
            if (Settings.GossipOn == null || !Settings.GossipOn.Value) return;
            if (listener == null || speaker == null || listener == speaker || listener.socUs == null || speaker.socUs == null) return;
            if (!listener.HasCond("IsHuman") || !speaker.HasCond("IsHuman")) return;
            if (listener == CrewSim.coPlayer && !Settings.GossipReachesPlayer.Value) return;
            if (Trust(toSpeaker) < MinTrust) return;

            string key = listener.strID + "|" + speaker.strID;
            double now = StarSystem.fEpoch;
            if (LastRoll.TryGetValue(key, out double last) && now >= last && now - last < Settings.GossipCooldownHours.Value * 3600.0) return;
            LastRoll[key] = now;

            Relationship opinion = Pick(listener, speaker);
            if (opinion == null) return;
            double heat = Math.Min(1.0, Math.Abs(Lean(opinion) * Tally.Totals(opinion).familiarity) / 100.0);
            double chance = Settings.GossipChance.Value * Talkativeness(speaker) * (0.5 + 0.5 * heat);
            if (Rng.NextDouble() < chance) Pending.Enqueue((listener, speaker, opinion));
        }

        internal static void Flush()
        {
            while (Pending.Count > 0)
            {
                (CondOwner listener, CondOwner speaker, Relationship opinion) = Pending.Dequeue();
                if (listener != null && speaker != null && opinion?.pspec != null && listener.bAlive && speaker.bAlive)
                    Spread(listener, speaker, opinion);
            }
        }

        private static void Spread(CondOwner listener, CondOwner speaker, Relationship opinion)
        {
            PersonSpec subject = opinion.pspec;
            Relationship mine = listener.socUs.GetRelationship(subject.FullName);
            Relationship toSpeaker = listener.socUs.GetRelationship(speaker.strName);
            double leanSpeaker = Lean(opinion);
            bool kindly = leanSpeaker <= 0;
            var rumor = new Rumor
            {
                Epoch = StarSystem.fEpoch, Speaker = speaker.FriendlyName, Listener = listener.FriendlyName,
                Subject = subject.FullName, Kindly = kindly,
            };

            double mineFamiliarity = mine != null && !Tally.Has(mine, "RELStranger") ? Tally.Totals(mine).familiarity : 0;
            double leanMine = mine != null ? Lean(mine) : 0;
            bool argues = Settings.GossipPushBack.Value && mineFamiliarity >= ArgueFrom
                          && Math.Abs(leanMine) >= ArgueLean && Math.Sign(leanMine) != Math.Sign(leanSpeaker);

            if (argues)
            {
                rumor.Reaction = "pushed back";
                // The same formula the other way: the listener's view rubs off on the speaker.
                Relationship toListener = speaker.socUs.GetRelationship(listener.strName);
                double back = Weight(speaker, toListener, listener, opinion);
                rumor.Moved = Transfer(speaker, mine, opinion, back);
                if (Personality && listener.HasCond("IsLoyal") && !kindly && IsClose(mine) && toSpeaker != null)
                {
                    Tally.Add(listener, toSpeaker, "StatEsteem", LoyalRespectCost);
                    Tally.Recompute(toSpeaker);
                }
            }
            else
            {
                double weight = Weight(listener, toSpeaker, speaker, mine);
                if (Size(opinion, weight) < MinEffect)
                    rumor.Reaction = "shrugged";
                else
                {
                    rumor.Reaction = "agreed";
                    mine ??= listener.socUs.AddStranger(subject);
                    rumor.Moved = Transfer(listener, opinion, mine, weight);
                    Tally.AddEvent(mine, Text.HeardAbout(speaker.FriendlyName));
                }
            }
            Remember(rumor);
            Tell(listener, speaker, subject, rumor);
        }

        /// <summary>How much of what <paramref name="teller"/> feels lands on <paramref name="hearer"/>: the formula in the summary.</summary>
        private static double Weight(CondOwner hearer, Relationship toTeller, CondOwner teller, Relationship hearerOnSubject)
        {
            double trust = Trust(toTeller);
            double experience = Experience(hearerOnSubject);
            return Settings.GossipShare.Value * trust * (1 - experience) * Credibility(hearer, toTeller, teller) * Exaggeration(teller);
        }

        /// <summary>
        /// Move <paramref name="to"/> toward <paramref name="from"/> by <paramref name="weight"/> of each
        /// capped entry, scaled down together to at most Cap familiarity. Returns the familiarity moved.
        /// </summary>
        private static double Transfer(CondOwner owner, Relationship from, Relationship to, double weight)
        {
            double size = Size(from, weight);
            if (to == null || size < MinEffect) return 0;
            double cap = Settings.GossipCap.Value;
            double scale = size > cap ? cap / size : 1.0;
            foreach (KeyValuePair<string, double> kv in from.Conds.ToList())
                Tally.Add(owner, to, kv.Key, weight * Tally.Clamp(kv.Value, Tally.PerStatCap) * scale);
            Tally.Recompute(to);
            return Math.Min(size, cap);
        }

        private static double Size(Relationship from, double weight) =>
            from == null ? 0 : from.Conds.Values.Sum(v => Math.Abs(weight * Tally.Clamp(v, Tally.PerStatCap)));

        /// <summary>
        /// How much someone regards a person, 0..1: the kind share above half, grown to full by
        /// familiarity 100, with floors for labels that mean trust whatever the numbers say.
        /// Enemies count for nothing.
        /// </summary>
        public static double Trust(Relationship r)
        {
            if (r == null || Tally.Has(r, "RELEnemy") || Tally.Has(r, "RELNemesis")) return 0;
            (double fam, double kind, _) = Tally.Totals(r);
            double t = fam > 0 ? Clamp01((kind / fam - 0.5) / 0.5) * Math.Min(1.0, fam / TrustFullAt) : 0;
            if (Tally.Has(r, "RELLover")) t = Math.Max(t, 0.8);
            if (Tally.Has(r, "RELFriend")) t = Math.Max(t, 0.6);
            if (Tally.IsFamily(r)) t = Math.Max(t, 0.5);
            return t;
        }

        /// <summary>Firsthand experience of the subject, 0..1. A stranger's familiarity is hearsay, so it counts as none.</summary>
        private static double Experience(Relationship r)
        {
            if (r == null || Tally.Has(r, "RELStranger")) return 0;
            double fam = Tally.Totals(r).familiarity;
            return fam / (fam + ExperienceScale);
        }

        /// <summary>Which way a relationship leans, −1 (all kind) to +1 (all hostile).</summary>
        private static double Lean(Relationship r)
        {
            (double fam, double kind, double hostile) = Tally.Totals(r);
            return fam > 0 ? (hostile - kind) / fam : 0;
        }

        private static bool IsClose(Relationship r) =>
            Tally.Has(r, "RELFriend") || Tally.Has(r, "RELLover") || Tally.IsFamily(r);

        private static bool Personality => Settings.PersonalityOn != null && Settings.PersonalityOn.Value;

        private static double Talkativeness(CondOwner speaker)
        {
            if (!Personality) return 1;
            if (speaker.HasCond("IsGregarious")) return 1.5;
            if (speaker.HasCond("IsShy")) return 0.5;
            return 1;
        }

        private static double Credibility(CondOwner hearer, Relationship toTeller, CondOwner teller)
        {
            if (!Personality) return 1;
            double c = 1;
            if (teller.HasCond("IsCharismatic")) c *= 1.25;
            if (Knows(toTeller, teller, "IsHonest")) c *= 1.25;
            if (Knows(toTeller, teller, "IsLiar")) c *= 0.5;
            if (hearer.HasCond("IsObservant")) c *= 0.8;
            if (hearer.HasCond("IsObtuse")) c *= 1.25;
            return c;
        }

        private static double Exaggeration(CondOwner teller) =>
            Personality && (teller.HasCond("IsLiar") || teller.HasCond("IsTreacherous")) ? 1.5 : 1;

        /// <summary>Whether a relationship has uncovered this trait of theirs (reveals list the slots; the trait must be real).</summary>
        private static bool Knows(Relationship r, CondOwner them, string trait) =>
            r?.aReveals != null && r.aReveals.Contains(trait) && them.HasCond(trait);

        /// <summary>
        /// Who the speaker brings up: someone they know well enough to have a real opinion of,
        /// weighted by how strongly they lean (familiarity × lean), doubled when the listener
        /// already knows them.
        /// </summary>
        private static Relationship Pick(CondOwner listener, CondOwner speaker)
        {
            var candidates = new List<(Relationship r, double w)>();
            foreach (Relationship r in speaker.socUs.GetAllPeople())
            {
                string name = r?.pspec?.FullName;
                if (name == null || name == listener.strName || name == speaker.strName) continue;
                (double fam, double kind, double hostile) = Tally.Totals(r);
                if (fam < MinOpinion) continue;
                double w = Math.Abs(kind - hostile);   // familiarity × lean
                if (listener.socUs.GetRelationship(name) != null) w *= 2;
                if (w > 0) candidates.Add((r, w));
            }
            double total = candidates.Sum(c => c.w);
            if (total <= 0) return null;
            double roll = Rng.NextDouble() * total;
            foreach ((Relationship r, double w) in candidates)
            {
                roll -= w;
                if (roll <= 0) return r;
            }
            return candidates[candidates.Count - 1].r;
        }

        private static void Tell(CondOwner listener, CondOwner speaker, PersonSpec subject, Rumor rumor)
        {
            CondOwner player = CrewSim.coPlayer;
            if (player == null || rumor.Reaction == "shrugged") return;
            if (listener == player)
            {
                if (rumor.Reaction == "agreed")
                    player.LogMessage(Text.ToldYouAbout(speaker.FriendlyName, subject.FullName), "Neutral", player.strName);
                return;
            }
            bool aboutPlayer = subject.GetCO() == player;
            bool present = player.currentRoom != null && (player.currentRoom == speaker.currentRoom || player.currentRoom == listener.currentRoom);
            if (!aboutPlayer || !present || !Settings.GossipLog.Value) return;
            string line = rumor.Reaction == "pushed back"
                ? Text.StoodUpForYou(listener.FriendlyName, speaker.FriendlyName, rumor.Kindly)
                : Text.TalkedAboutYou(speaker.FriendlyName, listener.FriendlyName, rumor.Kindly);
            player.LogMessage(line, "Neutral", player.strName);
        }

        private static void Remember(Rumor rumor)
        {
            Recent.Add(rumor);
            if (Recent.Count > Remembered) Recent.RemoveRange(0, Recent.Count - Remembered);
            Plugin.Log.LogInfo($"Gossip: {rumor.Speaker} → {rumor.Listener} about {rumor.Subject} "
                               + $"({(rumor.Kindly ? "kindly" : "unkindly")}): {rumor.Reaction}, {rumor.Moved:0.#} familiarity moved");
        }

        private static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));
    }

    [HarmonyPatch(typeof(Relationship), nameof(Relationship.StoreIAConds))]
    internal static class GossipPatch
    {
        private static void Postfix(Relationship __instance, CondOwner coUs, Dictionary<string, double> dict, CondOwner coThem)
        {
            if (dict == null || coUs == null || !coUs.bAlive || coUs.HasCond("Unconscious")) return;
            Gossip.Heard(coUs, __instance, coThem);
        }
    }
}
