using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace SocialMechanicsExpansion
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.sylvia.socialmechanicsexpansion";
        public const string Name = "Social Mechanics Expansion";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            Settings.ThawOn = Config.Bind("Thaw", "Enabled", true,
                "Relationships can change label before the game's familiarity 200: friend or enemy is decided sooner, "
                + "and an enemy who's since been treated kindly can soften to an acquaintance (a friend treated badly, likewise).");
            Settings.VerdictAt = Config.Bind("Thaw", "VerdictAt", 120f,
                new ConfigDescription(
                    "Familiarity at which friend or enemy is decided, by the game's own 55% rule. The game uses 200; "
                    + "strangers become acquaintances at 50.",
                    new AcceptableValueRange<float>(50f, 200f)));
            Settings.ThawAt = Config.Bind("Thaw", "ThawAt", 60f,
                new ConfigDescription(
                    "Familiarity from which an enemy can soften to an acquaintance, or a friend cool to one.",
                    new AcceptableValueRange<float>(0f, 200f)));
            Settings.ThawShare = Config.Bind("Thaw", "ThawShare", 0.55f,
                new ConfigDescription(
                    "Share of the relationship that must have gone the other way: kind for an enemy to soften, hostile for "
                    + "a friend to cool.",
                    new AcceptableValueRange<float>(0.5f, 1f)));

            Settings.DriftOn = Config.Bind("Drift", "Enabled", true,
                "Feelings fade with time apart: every relationship's tally eases back toward neutral, hostility faster than kindness. "
                + "Labels only change at the next real exchange.");
            Settings.HostileHalfLifeDays = Config.Bind("Drift", "HostileHalfLifeDays", 20f,
                new ConfigDescription("Game days for the hostile part of a relationship to fade by half.",
                    new AcceptableValueRange<float>(1f, 1000f)));
            Settings.KindHalfLifeDays = Config.Bind("Drift", "KindHalfLifeDays", 40f,
                new ConfigDescription("Game days for the kind part of a relationship to fade by half.",
                    new AcceptableValueRange<float>(1f, 1000f)));
            Settings.PauseWhenTogether = Config.Bind("Drift", "PauseWhenTogether", true,
                "Nothing fades between two people while they're in the same room.");

            Settings.GossipOn = Config.Bind("Gossip", "Enabled", true,
                "Friends pass on how they feel about other people, so opinions (and reputations) travel.");
            Settings.GossipChance = Config.Bind("Gossip", "Chance", 0.25f,
                new ConfigDescription("Chance, once per pair per cooldown, that a friend passes on an opinion while talking.",
                    new AcceptableValueRange<float>(0f, 1f)));
            Settings.GossipShare = Config.Bind("Gossip", "Share", 0.10f,
                new ConfigDescription("How much of the speaker's feeling about someone rubs off on the listener.",
                    new AcceptableValueRange<float>(0f, 1f)));
            Settings.GossipCap = Config.Bind("Gossip", "Cap", 10f,
                new ConfigDescription("Most one rumor can move the listener's familiarity with its subject.",
                    new AcceptableValueRange<float>(0f, 50f)));
            Settings.GossipCooldownHours = Config.Bind("Gossip", "CooldownHours", 24f,
                new ConfigDescription("Game hours between one pair's chances to gossip.",
                    new AcceptableValueRange<float>(0f, 1000f)));
            Settings.GossipReachesPlayer = Config.Bind("Gossip", "ReachesPlayer", false,
                "Off: gossip never changes how the player character feels about anyone. On: it can, like anyone else.");
            Settings.GossipLog = Config.Bind("Gossip", "LogAboutPlayer", true,
                "Tell the player when someone in their room passes on an opinion about them.");

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private string _lastError;

        private void Update()
        {
            try
            {
                Gossip.Flush();
                Drift.Tick();
            }
            catch (Exception e)
            {
                // Once per distinct failure, so a broken tick doesn't flood the log every frame.
                if (e.ToString() != _lastError) Log.LogError(e);
                _lastError = e.ToString();
            }
        }
    }

    internal static class Settings
    {
        internal static ConfigEntry<bool> ThawOn;
        internal static ConfigEntry<float> VerdictAt;
        internal static ConfigEntry<float> ThawAt;
        internal static ConfigEntry<float> ThawShare;

        internal static ConfigEntry<bool> DriftOn;
        internal static ConfigEntry<float> HostileHalfLifeDays;
        internal static ConfigEntry<float> KindHalfLifeDays;
        internal static ConfigEntry<bool> PauseWhenTogether;

        internal static ConfigEntry<bool> GossipOn;
        internal static ConfigEntry<float> GossipChance;
        internal static ConfigEntry<float> GossipShare;
        internal static ConfigEntry<float> GossipCap;
        internal static ConfigEntry<float> GossipCooldownHours;
        internal static ConfigEntry<bool> GossipReachesPlayer;
        internal static ConfigEntry<bool> GossipLog;
    }
}
