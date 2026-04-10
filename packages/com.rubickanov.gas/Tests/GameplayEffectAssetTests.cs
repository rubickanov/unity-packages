using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.GAS.Tests
{
    [TestFixture]
    public class GameplayEffectAssetTests
    {
        private GameplayEffectAsset _asset = null!;

        [SetUp]
        public void SetUp()
        {
            GasTestFixtures.InstallGasRegistry();
            _asset = ScriptableObject.CreateInstance<GameplayEffectAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_asset != null)
                Object.DestroyImmediate(_asset);
            GasTestFixtures.EnsureUninstalled();
        }

        private void LoadAsset(string json)
        {
            JsonUtility.FromJsonOverwrite(json, _asset);
        }

        private static string BuildJson(
            DurationPolicy duration = DurationPolicy.Duration,
            float durationSeconds = 5f,
            float period = 0f,
            StackingPolicy stacking = StackingPolicy.Independent,
            string effectTag = "Effect.Burn",
            string[]? grantedTags = null,
            string[]? requiredTags = null,
            string[]? blockedTags = null,
            string[]? removeEffectsWithTags = null,
            (string attr, ModifierOp op, float value)[]? modifiers = null)
        {
            string PathsJson(string[]? arr)
            {
                if (arr == null || arr.Length == 0) return "[]";
                var items = new string[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                    items[i] = $"\"{arr[i]}\"";
                return "[" + string.Join(",", items) + "]";
            }

            string TagJson(string? path)
                => $"{{\"_path\":\"{path ?? ""}\"}}";

            string ContainerJson(string[]? paths)
                => $"{{\"_paths\":{PathsJson(paths)}}}";

            string ModifiersJson()
            {
                if (modifiers == null || modifiers.Length == 0) return "[]";
                var items = new string[modifiers.Length];
                for (int i = 0; i < modifiers.Length; i++)
                {
                    var m = modifiers[i];
                    items[i] = "{" +
                               $"\"_attribute\":{TagJson(m.attr)}," +
                               $"\"_operation\":{(int)m.op}," +
                               $"\"_value\":{m.value.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                               "}";
                }
                return "[" + string.Join(",", items) + "]";
            }

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return "{" +
                   $"\"_duration\":{(int)duration}," +
                   $"\"_durationSeconds\":{durationSeconds.ToString(inv)}," +
                   $"\"_period\":{period.ToString(inv)}," +
                   $"\"_stacking\":{(int)stacking}," +
                   $"\"_modifiers\":{ModifiersJson()}," +
                   $"\"_effectTag\":{TagJson(effectTag)}," +
                   $"\"_grantedTags\":{ContainerJson(grantedTags)}," +
                   $"\"_requiredTags\":{ContainerJson(requiredTags)}," +
                   $"\"_blockedTags\":{ContainerJson(blockedTags)}," +
                   $"\"_removeEffectsWithTags\":{ContainerJson(removeEffectsWithTags)}" +
                   "}";
        }

        [Test]
        public void ToDef_DurationPolicyMatchesAsset()
        {
            LoadAsset(BuildJson(duration: DurationPolicy.Infinite));

            var def = _asset.ToDef();

            Assert.AreEqual(DurationPolicy.Infinite, def.Duration);
        }

        [Test]
        public void ToDef_DurationSecondsMatchesAsset()
        {
            LoadAsset(BuildJson(durationSeconds: 12.5f));

            var def = _asset.ToDef();

            Assert.That(def.DurationSeconds, Is.EqualTo(12.5f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void ToDef_PeriodMatchesAsset()
        {
            LoadAsset(BuildJson(period: 0.25f));

            var def = _asset.ToDef();

            Assert.That(def.Period, Is.EqualTo(0.25f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void ToDef_StackingMatchesAsset()
        {
            LoadAsset(BuildJson(stacking: StackingPolicy.Replace));

            var def = _asset.ToDef();

            Assert.AreEqual(StackingPolicy.Replace, def.Stacking);
        }

        [Test]
        public void ToDef_EffectTagResolvesFromRegistry()
        {
            LoadAsset(BuildJson(effectTag: "Effect.Heal"));

            var def = _asset.ToDef();

            Assert.AreEqual(GasTestFixtures.Tag("Effect.Heal"), def.EffectTag);
        }

        [Test]
        public void ToDef_ModifiersListPopulatedFromSerializedModifiers()
        {
            LoadAsset(BuildJson(modifiers: new[]
            {
                ("Attribute.Health", ModifierOp.Add, 10f),
                ("Attribute.Speed", ModifierOp.Multiply, 2f)
            }));

            var def = _asset.ToDef();

            Assert.AreEqual(2, def.Modifiers.Count);
            Assert.AreEqual(GasTestFixtures.Tag("Attribute.Health"), def.Modifiers[0].Attribute);
            Assert.AreEqual(ModifierOp.Add, def.Modifiers[0].Operation);
            Assert.That(def.Modifiers[0].Value, Is.EqualTo(10f).Within(GasTestFixtures.FloatTolerance));
            Assert.AreEqual(GasTestFixtures.Tag("Attribute.Speed"), def.Modifiers[1].Attribute);
            Assert.AreEqual(ModifierOp.Multiply, def.Modifiers[1].Operation);
            Assert.That(def.Modifiers[1].Value, Is.EqualTo(2f).Within(GasTestFixtures.FloatTolerance));
        }

        [Test]
        public void ToDef_GrantedTagsResolveFromRegistry()
        {
            LoadAsset(BuildJson(grantedTags: new[] { "Status.Stun", "Debuff.Burn" }));

            var def = _asset.ToDef();

            Assert.IsTrue(def.GrantedTags.HasTagExact(GasTestFixtures.Tag("Status.Stun")));
            Assert.IsTrue(def.GrantedTags.HasTagExact(GasTestFixtures.Tag("Debuff.Burn")));
        }

        [Test]
        public void ToDef_RequiredTagsResolveFromRegistry()
        {
            LoadAsset(BuildJson(requiredTags: new[] { "Damage.Fire" }));

            var def = _asset.ToDef();

            Assert.IsTrue(def.ApplicationRequiredTags.HasTagExact(GasTestFixtures.Tag("Damage.Fire")));
        }

        [Test]
        public void ToDef_BlockedTagsResolveFromRegistry()
        {
            LoadAsset(BuildJson(blockedTags: new[] { "Immune" }));

            var def = _asset.ToDef();

            Assert.IsTrue(def.ApplicationBlockedTags.HasTagExact(GasTestFixtures.Tag("Immune")));
        }

        [Test]
        public void ToDef_RemoveEffectsWithTagsResolveFromRegistry()
        {
            LoadAsset(BuildJson(removeEffectsWithTags: new[] { "Debuff" }));

            var def = _asset.ToDef();

            Assert.IsTrue(def.RemoveEffectsWithTags.HasTagExact(GasTestFixtures.Tag("Debuff")));
        }

        [Test]
        public void ToDef_EmptyModifiers_ReturnsEmptyModifierList()
        {
            LoadAsset(BuildJson());

            var def = _asset.ToDef();

            Assert.AreEqual(0, def.Modifiers.Count);
        }

        [Test]
        public void CreateSpec_DefaultMagnitude_IsOne()
        {
            LoadAsset(BuildJson());

            var spec = _asset.CreateSpec();

            Assert.That(spec.Magnitude, Is.EqualTo(1f).Within(GasTestFixtures.FloatTolerance));
            Assert.IsNull(spec.Source);
        }

        [Test]
        public void CreateSpec_WithSource_PropagatesSource()
        {
            LoadAsset(BuildJson());
            var source = new object();

            var spec = _asset.CreateSpec(source);

            Assert.AreSame(source, spec.Source);
        }

        [Test]
        public void CreateSpec_CustomMagnitude_PropagatesMagnitude()
        {
            LoadAsset(BuildJson());

            var spec = _asset.CreateSpec(magnitude: 2.5f);

            Assert.That(spec.Magnitude, Is.EqualTo(2.5f).Within(GasTestFixtures.FloatTolerance));
        }
    }
}
