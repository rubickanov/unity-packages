using Rubickanov.GameplayTags;

namespace Rubickanov.GAS.Tests
{
    internal static class GasTestFixtures
    {
        public const float FloatTolerance = 1e-4f;

        public static readonly string[] GasTagPaths =
        {
            "Damage",
            "Damage.Fire",
            "Damage.Fire.DoT",
            "Damage.Ice",
            "Status",
            "Status.Stun",
            "Status.Burning",
            "Immune",
            "Immune.Stun",
            "Debuff",
            "Debuff.Burn",
            "Attribute",
            "Attribute.Health",
            "Attribute.Speed",
            "Effect",
            "Effect.Burn",
            "Effect.Heal",
            "Effect.Buff",
            "Effect.Buff.Speed"
        };

        public static GameplayTagRegistry BuildGasRegistry()
            => new GameplayTagRegistry(GasTagPaths);

        public static void InstallGasRegistry()
        {
            EnsureUninstalled();
            GameplayTagRegistry.Install(BuildGasRegistry());
        }

        public static void EnsureUninstalled()
        {
            if (GameplayTagRegistry.IsInstalled)
                GameplayTagRegistry.Uninstall();
        }

        public static GameplayTag Tag(string path)
            => GameplayTagRegistry.Instance.Get(path);

        public static GameplayTagContainer Container(params string[] paths)
        {
            var container = new GameplayTagContainer();
            foreach (var path in paths)
                container.AddTag(Tag(path));
            return container;
        }

        public static Modifier Mod(string attributePath, ModifierOp op, float value, int priority = 0)
            => new Modifier(Tag(attributePath), op, value, priority);

        public static EffectDef MakeEffect(
            DurationPolicy duration = DurationPolicy.Instant,
            float durationSeconds = 0f,
            float period = 0f,
            Modifier[]? modifiers = null,
            string[]? grantedTags = null,
            string[]? requiredTags = null,
            string[]? blockedTags = null,
            string[]? removeEffectsWithTags = null,
            string? effectTag = null,
            StackingPolicy stacking = StackingPolicy.Independent)
        {
            return new EffectDef(
                duration,
                durationSeconds,
                period,
                modifiers ?? System.Array.Empty<Modifier>(),
                BuildContainer(grantedTags),
                BuildContainer(requiredTags),
                BuildContainer(blockedTags),
                BuildContainer(removeEffectsWithTags),
                effectTag != null ? Tag(effectTag) : GameplayTag.None,
                stacking);
        }

        public static (AttributeSet attributes, GameplayTagContainer tags, EffectController controller)
            MakeTargetWithHealth(float baseHealth = 100f, float baseSpeed = 10f)
        {
            var attributes = new AttributeSet();
            attributes.Define(Tag("Attribute.Health"), baseHealth);
            attributes.Define(Tag("Attribute.Speed"), baseSpeed);

            var tags = new GameplayTagContainer();
            var controller = new EffectController(attributes, tags);
            return (attributes, tags, controller);
        }

        private static GameplayTagContainer BuildContainer(string[]? paths)
        {
            var container = new GameplayTagContainer();
            if (paths == null) return container;
            foreach (var path in paths)
                container.AddTag(Tag(path));
            return container;
        }
    }
}
