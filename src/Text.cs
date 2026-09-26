namespace SocialMechanicsExpansion
{
    /// <summary>
    /// Every string a player reads. Placeholders for now: Sylvia writes the real ones.
    /// Relationship history lines follow the game's own "Became Enemy during: Serve Hard Time".
    /// </summary>
    internal static class Text
    {
        // Relationship history (shown in the game's relationship list).
        public const string MadePeace = "Made peace";
        public const string FellOut = "Fell out";
        public static string HeardAbout(string speaker) => $"Heard about them from {speaker}";

        // Log lines for the player.
        public static string TalkedAboutYou(string speaker, string listener, bool kindly) =>
            kindly ? $"{speaker} speaks well of you to {listener}." : $"{speaker} runs you down to {listener}.";
        public static string ToldYouAbout(string speaker, string subject) => $"{speaker} tells you about {subject}.";
    }
}
