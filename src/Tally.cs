using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace SocialMechanicsExpansion
{
    /// <summary>
    /// Reading and editing a relationship's tally the way the game keeps it.
    ///
    /// Each person holds one Relationship per acquaintance, one-way. Its tally (dictConds) has
    /// an entry per social stat: every exchange adds half of what it did to that stat, negative
    /// when the other person eased it (kind), positive when they worsened it (hostile).
    /// Relationship.StoreIACond totals it: each entry capped at ±25, familiarity is the sum of
    /// the capped sizes, kindness the negative part, animosity the positive part.
    ///
    /// The trap: while someone's attention is on a person (CondOwner.strLastSocial), the game
    /// adds that relationship's entries (capped at ±50) onto their live stats, and takes them
    /// off again when attention moves (Relationship.ApplyConds). An entry changed in between
    /// would come off at a different size than it went on, leaving the stat off for good. So
    /// every change here goes through <see cref="Add"/>, which moves the live stat by the same
    /// amount ApplyConds would have.
    /// </summary>
    public static class Tally
    {
        public const double PerStatCap = 25.0;     // Relationship.fMaxPerIA
        private const double AppliedCap = 50.0;    // Relationship.fMax

        private static readonly AccessTools.FieldRef<Relationship, double> Kindness =
            AccessTools.FieldRefAccess<Relationship, double>("fKindness");
        private static readonly AccessTools.FieldRef<Relationship, double> Animosity =
            AccessTools.FieldRefAccess<Relationship, double>("fAnimosity");
        private static readonly AccessTools.FieldRef<CondOwner, string> LastSocial =
            AccessTools.FieldRefAccess<CondOwner, string>("strLastSocial");

        /// <summary>Familiarity, kindness and animosity, totalled the way Relationship.StoreIACond does.</summary>
        public static (double familiarity, double kind, double hostile) Totals(Relationship r)
        {
            double fam = 0, kind = 0, hostile = 0;
            Dictionary<string, double> conds = r?.Conds;
            if (conds == null) return (0, 0, 0);
            foreach (double raw in conds.Values)
            {
                double v = Clamp(raw, PerStatCap);
                fam += Math.Abs(v);
                if (v < 0) kind -= v;
                else hostile += v;
            }
            return (fam, kind, hostile);
        }

        public static double KindShare(Relationship r)
        {
            (double fam, double kind, _) = Totals(r);
            return fam > 0 ? kind / fam : 0;
        }

        /// <summary>Change one entry of <paramref name="owner"/>'s tally about someone, keeping their live stats honest.</summary>
        public static void Add(CondOwner owner, Relationship r, string stat, double delta)
        {
            if (r == null || stat == null || delta == 0 || double.IsNaN(delta)) return;
            Dictionary<string, double> conds = r.Conds;
            conds.TryGetValue(stat, out double before);
            double after = before + delta;
            conds[stat] = after;
            if (IsFocused(owner, r))
            {
                double live = Clamp(after, AppliedCap) - Clamp(before, AppliedCap);
                if (live != 0) owner.AddCondAmount(stat, live);
            }
        }

        /// <summary>Refresh the stored totals after editing, so anything reading them before the next exchange sees the truth.</summary>
        public static void Recompute(Relationship r)
        {
            if (r == null) return;
            (double fam, double kind, double hostile) = Totals(r);
            r.fFamiliarity = fam;
            Kindness(r) = kind;
            Animosity(r) = hostile;
        }

        /// <summary>Whether this relationship is the one currently applied to <paramref name="owner"/>'s stats.</summary>
        public static bool IsFocused(CondOwner owner, Relationship r) =>
            owner != null && r?.pspec != null && LastSocial(owner) == r.pspec.FullName;

        public static bool Has(Relationship r, string label) => r?.aRelationships != null && r.aRelationships.Contains(label);

        public static bool IsFamily(Relationship r) =>
            r?.aRelationships != null && r.aRelationships.Any(l => l != null && l.StartsWith("RELBio", StringComparison.Ordinal));

        public static void AddEvent(Relationship r, string text)
        {
            if (r == null || string.IsNullOrEmpty(text)) return;
            if (r.aEvents == null) r.aEvents = new List<string>();
            if (!r.aEvents.Contains(text)) r.aEvents.Add(text);
        }

        public static double Clamp(double v, double cap) => Math.Max(-cap, Math.Min(cap, v));
    }
}
