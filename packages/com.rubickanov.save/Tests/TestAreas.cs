namespace Rubickanov.Save.Tests
{
    /// <summary>A game's areas, laid out as one that kept files before the store: documents at the root, the rest in folders.</summary>
    internal static class TestAreas
    {
        public static readonly SaveArea Settings = SaveArea.AtRoot("Settings", "settings");
        public static readonly SaveArea Progress = SaveArea.AtRoot("Progress", "progress");
        public static readonly SaveArea Controls = SaveArea.AtRoot("Controls", "controls");
        public static readonly SaveArea Replays = SaveArea.InFolder("Replays");
        public static readonly SaveArea Seasons = SaveArea.InFolder("Seasons");
        public static readonly SaveArea Covers = SaveArea.InFolder("Covers");
    }
}
