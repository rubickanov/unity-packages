namespace Rubickanov.GameplayTags.Tests
{
    /// <summary>
    /// Shared helpers for gameplaytags tests: canonical tag list, registry lifecycle, container builders.
    /// </summary>
    internal static class TagTestFixtures
    {
        public static readonly string[] StandardTagPaths =
        {
            "Attribute",
            "Attribute.Health",
            "Attribute.Speed",
            "Damage",
            "Damage.Fire",
            "Damage.Fire.DoT",
            "Damage.Ice",
            "Debuff",
            "Debuff.Burn",
            "Immune",
            "Immune.Stun",
            "Status",
            "Status.Burning",
            "Status.Stun"
        };

        public static GameplayTagRegistry BuildStandardRegistry() => new(StandardTagPaths);

        public static void InstallStandardRegistry()
        {
            EnsureUninstalled();
            GameplayTagRegistry.Install(BuildStandardRegistry());
        }

        public static void EnsureUninstalled()
        {
            GameplayTagRegistry.Uninstall();
        }

        public static GameplayTag Tag(string path) => GameplayTagRegistry.Instance.Get(path);

        public static GameplayTagContainer Container(params string[] paths)
        {
            var container = new GameplayTagContainer(paths.Length);
            foreach (var path in paths)
                container.AddTag(Tag(path));
            return container;
        }
    }
}
