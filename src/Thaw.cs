using System.Collections.Generic;
using HarmonyLib;

namespace SocialMechanicsExpansion
{
    /// <summary>
    /// Labels that can change. After every exchange, Relationship.StoreIAConds re-reads the
    /// tally and flips labels, but it only decides friend or enemy at familiarity 200 and never
    /// takes an enemy back. Thaw runs right after it, on the same one-way relationship:
    ///
    ///   familiarity ≥ VerdictAt: the game's own verdict, reached sooner (over 55% hostile →
    ///     enemy, over 55% kind → friend);
    ///   familiarity ≥ ThawAt: an enemy now treated kindly past ThawShare softens to an
    ///     acquaintance ("made peace"); a friend treated badly past it cools to one ("fell out").
    ///
    /// Strangers, nemeses and family labels are left to the game.
    /// </summary>
    public static class Thaw
    {
        private const double GameVerdictShare = 0.55;

        /// <summary>What the next exchange would do to this label under these rules, or null. For tools such as OstraScope.</summary>
        public static string Next(Relationship r) => Decide(r).describe;

        internal static void Apply(Relationship r, CondOwner owner)
        {
            (string[] remove, string add, string ev, _) = Decide(r);
            if (add == null) return;
            foreach (string label in remove) r.RemoveRelationship(owner, label);
            r.AddRelationship(owner, add);
            if (ev != null) Tally.AddEvent(r, ev);
        }

        private static readonly string[] None = new string[0];

        private static (string[] remove, string add, string ev, string describe) Decide(Relationship r)
        {
            if (Settings.ThawOn == null || !Settings.ThawOn.Value || r?.aRelationships == null) return (None, null, null, null);
            if (Tally.Has(r, "RELStranger") || Tally.Has(r, "RELNemesis")) return (None, null, null, null);
            (double fam, double kind, double hostile) = Tally.Totals(r);
            if (fam <= 0) return (None, null, null, null);
            double kindShare = kind / fam, hostileShare = hostile / fam;

            if (fam >= Settings.VerdictAt.Value)
            {
                if (hostileShare > GameVerdictShare && !Tally.Has(r, "RELEnemy"))
                    return (new[] { "RELAcquaintance", "RELFriend" }, "RELEnemy", null, "becomes an enemy at the next exchange");
                if (hostileShare <= GameVerdictShare && kindShare > GameVerdictShare && !Tally.Has(r, "RELFriend"))
                    return (new[] { "RELAcquaintance", "RELEnemy" }, "RELFriend", null, "becomes a friend at the next exchange");
            }
            else if (fam >= Settings.ThawAt.Value)
            {
                if (Tally.Has(r, "RELEnemy") && kindShare > Settings.ThawShare.Value)
                    return (new[] { "RELEnemy" }, "RELAcquaintance", Text.MadePeace, "softens from enemy to acquaintance at the next exchange");
                if (Tally.Has(r, "RELFriend") && hostileShare > Settings.ThawShare.Value)
                    return (new[] { "RELFriend" }, "RELAcquaintance", Text.FellOut, "cools from friend to acquaintance at the next exchange");
            }
            return (None, null, null, null);
        }
    }

    [HarmonyPatch(typeof(Relationship), nameof(Relationship.StoreIAConds))]
    internal static class ThawPatch
    {
        private static void Postfix(Relationship __instance, CondOwner coUs, Dictionary<string, double> dict)
        {
            // The game's own guards: nothing is stored for the dead or unconscious.
            if (dict == null || coUs == null || !coUs.bAlive || coUs.HasCond("Unconscious")) return;
            Thaw.Apply(__instance, coUs);
        }
    }
}
