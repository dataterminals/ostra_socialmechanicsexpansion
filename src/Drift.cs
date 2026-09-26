using System;
using System.Collections.Generic;
using System.Linq;

namespace SocialMechanicsExpansion
{
    /// <summary>
    /// Time apart fades feelings. About once a game hour, every loaded person's tallies ease
    /// toward neutral: the hostile entries with a half-life of HostileHalfLifeDays, the kind ones
    /// with KindHalfLifeDays, so grudges cool sooner than friendships do. Two people in the
    /// same room don't drift apart while they're there. With Personality on, a Forgiving person's
    /// grudges fade twice as fast.
    ///
    /// Only the tally moves; labels wait for the next real exchange, where the game (and Thaw)
    /// re-read it. An NPC's feelings only drift while they're loaded, which is whenever the
    /// player is on their station or ship. The player's own feelings always drift.
    /// </summary>
    public static class Drift
    {
        private const double StepSeconds = 3600;            // one game hour
        private const double MaxStepSeconds = 7 * 86400;    // a long skip counts as a week at most
        private const double Negligible = 0.01;

        private static double _last = double.NaN;
        private static CondOwner _player;

        internal static void Tick()
        {
            CondOwner player = CrewSim.coPlayer;
            if (Settings.DriftOn == null || !Settings.DriftOn.Value || player == null)
            {
                _last = double.NaN;
                return;
            }
            double now = StarSystem.fEpoch;
            // A new or reloaded game: start counting from here rather than from the old clock.
            if (player != _player || double.IsNaN(_last) || now < _last)
            {
                _player = player;
                _last = now;
                return;
            }
            double dt = now - _last;
            if (dt < StepSeconds) return;
            _last = now;
            Apply(Math.Min(dt, MaxStepSeconds));
        }

        private static void Apply(double seconds)
        {
            double hostileNormal = Factor(seconds, Settings.HostileHalfLifeDays.Value);
            double hostileForgiving = Factor(seconds, Settings.HostileHalfLifeDays.Value / 2);
            double kind = Factor(seconds, Settings.KindHalfLifeDays.Value);
            bool personality = Settings.PersonalityOn != null && Settings.PersonalityOn.Value;
            foreach (CondOwner co in People())
            {
                double hostile = personality && co.HasCond("IsForgiving") ? hostileForgiving : hostileNormal;
                foreach (Relationship r in co.socUs.GetAllPeople())
                {
                    if (r?.pspec == null) continue;
                    if (Settings.PauseWhenTogether.Value && Together(co, r.pspec.GetCO())) continue;
                    Dictionary<string, double> conds = r.Conds;
                    bool changed = false;
                    foreach (string stat in conds.Keys.ToList())
                    {
                        double v = conds[stat];
                        if (v == 0) continue;
                        double target = Math.Abs(v) < Negligible ? 0 : v * (v > 0 ? hostile : kind);
                        Tally.Add(co, r, stat, target - v);
                        changed = true;
                    }
                    if (changed) Tally.Recompute(r);
                }
            }
        }

        private static double Factor(double seconds, float halfLifeDays) =>
            Math.Pow(0.5, seconds / (Math.Max(0.001, halfLifeDays) * 86400.0));

        private static IEnumerable<CondOwner> People()
        {
            if (DataHandler.mapCOs == null) yield break;
            foreach (CondOwner co in DataHandler.mapCOs.Values.ToList())
                if (co != null && co.socUs != null && co.bAlive && co.HasCond("IsHuman"))
                    yield return co;
        }

        private static bool Together(CondOwner a, CondOwner b) =>
            a != null && b != null && a.currentRoom != null && a.currentRoom == b.currentRoom;
    }
}
