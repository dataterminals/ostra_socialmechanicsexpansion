using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace SocialMechanicsExpansion
{
    /// <summary>
    /// Opinions travel. Relationship.StoreIAConds runs on whoever an exchange just affected
    /// (the listener) with the person who affected them (the speaker). When the listener trusts
    /// the speaker (family, a friend, a crush, or a kind acquaintance), the pair gets one roll per
    /// CooldownHours at GossipChance. On a hit the speaker passes on how they feel about one
    /// third person: weighted toward whoever they feel most strongly about, and toward people the
    /// listener already knows.
    ///
    /// The listener's tally on that person moves by Share of the speaker's, capped at Cap
    /// familiarity per rumor. Someone the listener never met is added as a stranger who's been
    /// heard about. Labels still wait for a real meeting, so meeting someone whose friend hates
    /// you starts off frosty.
    ///
    /// Rumors are applied on the next frame, outside the game's exchange.
    /// </summary>
    public static class Gossip
    {
        private const double MinOpinion = 50.0;     // the speaker must know them at least as an acquaintance would
        private const int Remembered = 20;

        public sealed class Rumor
        {
            public double Epoch;
            public string Speaker, Listener, Subject;
            public bool Kindly;
        }

        private static readonly Queue<(CondOwner listener, CondOwner speaker)> Pending = new Queue<(CondOwner, CondOwner)>();
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
            if (!Trusts(toSpeaker)) return;

            string key = listener.strID + "|" + speaker.strID;
            double now = StarSystem.fEpoch;
            if (LastRoll.TryGetValue(key, out double last) && now >= last && now - last < Settings.GossipCooldownHours.Value * 3600.0) return;
            LastRoll[key] = now;
            if (Rng.NextDouble() < Settings.GossipChance.Value) Pending.Enqueue((listener, speaker));
        }

        internal static void Flush()
        {
            while (Pending.Count > 0)
            {
                (CondOwner listener, CondOwner speaker) = Pending.Dequeue();
                if (listener != null && speaker != null && listener.bAlive && speaker.bAlive) Spread(listener, speaker);
            }
        }

        private static bool Trusts(Relationship r)
        {
            if (r == null || Tally.Has(r, "RELEnemy") || Tally.Has(r, "RELNemesis")) return false;
            if (Tally.Has(r, "RELFriend") || Tally.Has(r, "RELLover") || Tally.IsFamily(r)) return true;
            (double fam, double kind, _) = Tally.Totals(r);
            return fam >= MinOpinion && kind / fam > 0.55;
        }

        private static void Spread(CondOwner listener, CondOwner speaker)
        {
            Relationship opinion = Pick(listener, speaker);
            if (opinion == null) return;
            PersonSpec subject = opinion.pspec;

            Relationship mine = listener.socUs.GetRelationship(subject.FullName) ?? listener.socUs.AddStranger(subject);
            if (mine == null) return;

            // Scale the whole rumor down together so it never moves familiarity by more than Cap.
            Dictionary<string, double> theirs = opinion.Conds;
            double share = Settings.GossipShare.Value;
            double size = theirs.Values.Sum(v => Math.Abs(share * Tally.Clamp(v, Tally.PerStatCap)));
            double scale = size > Settings.GossipCap.Value && size > 0 ? Settings.GossipCap.Value / size : 1.0;
            foreach (KeyValuePair<string, double> kv in theirs.ToList())
                Tally.Add(listener, mine, kv.Key, share * Tally.Clamp(kv.Value, Tally.PerStatCap) * scale);
            Tally.Recompute(mine);
            Tally.AddEvent(mine, Text.HeardAbout(speaker.FriendlyName));

            (_, double kind, double hostile) = Tally.Totals(opinion);
            bool kindly = kind >= hostile;
            Remember(new Rumor
            {
                Epoch = StarSystem.fEpoch, Speaker = speaker.FriendlyName, Listener = listener.FriendlyName,
                Subject = subject.FullName, Kindly = kindly,
            });

            CondOwner player = CrewSim.coPlayer;
            if (player == null) return;
            if (listener == player)
                player.LogMessage(Text.ToldYouAbout(speaker.FriendlyName, subject.FullName), "Neutral", player.strName);
            else if (Settings.GossipLog.Value && subject.GetCO() == player && player.currentRoom != null
                     && (player.currentRoom == speaker.currentRoom || player.currentRoom == listener.currentRoom))
                player.LogMessage(Text.TalkedAboutYou(speaker.FriendlyName, listener.FriendlyName, kindly), "Neutral", player.strName);
        }

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

        private static void Remember(Rumor rumor)
        {
            Recent.Add(rumor);
            if (Recent.Count > Remembered) Recent.RemoveRange(0, Recent.Count - Remembered);
            Plugin.Log.LogInfo($"Gossip: {rumor.Speaker} → {rumor.Listener} about {rumor.Subject} ({(rumor.Kindly ? "kindly" : "unkindly")})");
        }
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
