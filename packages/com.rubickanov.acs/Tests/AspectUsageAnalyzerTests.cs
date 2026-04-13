using NUnit.Framework;
using Rubickanov.ACS.Editor;

namespace Rubickanov.ACS.Tests
{
    [TestFixture]
    public class AspectUsageAnalyzerTests
    {
        [TestFixture]
        public class ParseRequiredAspects
        {
            [Test]
            public void ParseRequiredAspects_ContextReceiver_DetectsAspect()
            {
                const string source = "var a = Context.Require<HealthAspect>();";

                var result = AspectUsageAnalyzer.ParseRequiredAspects(source);

                Assert.Contains("HealthAspect", result);
            }

            [Test]
            public void ParseRequiredAspects_WorldReceiver_DetectsAspect()
            {
                const string source = "var t = World.Require<TimeAspect>();";

                var result = AspectUsageAnalyzer.ParseRequiredAspects(source);

                Assert.Contains("TimeAspect", result);
            }

            [Test]
            public void ParseRequiredAspects_EntityVariableReceiver_DetectsAspect()
            {
                const string source = "var h = entity.Require<HealthAspect>();";

                var result = AspectUsageAnalyzer.ParseRequiredAspects(source);

                Assert.Contains("HealthAspect", result);
            }

            [Test]
            public void ParseRequiredAspects_NestedReceiver_DetectsAspect()
            {
                const string source = "var x = _ctx.inner.Require<StatusAspect>();";

                var result = AspectUsageAnalyzer.ParseRequiredAspects(source);

                Assert.Contains("StatusAspect", result);
            }

            [Test]
            public void ParseRequiredAspects_AspectAttribute_DetectsAspect()
            {
                const string source = "[Aspect] private readonly HealthAspect _health = default!;";

                var result = AspectUsageAnalyzer.ParseRequiredAspects(source);

                Assert.Contains("HealthAspect", result);
            }

            [Test]
            public void ParseRequiredAspects_MixedReceiversAndAttribute_AllDetectedOnce()
            {
                const string source =
                    "[Aspect] private readonly HealthAspect _health;\n" +
                    "var w = World.Require<TimeAspect>();\n" +
                    "var c = Context.Require<HealthAspect>();";

                var result = AspectUsageAnalyzer.ParseRequiredAspects(source);

                Assert.Contains("HealthAspect", result);
                Assert.Contains("TimeAspect", result);
                Assert.AreEqual(2, result.Count);
            }
        }

        [TestFixture]
        public class FindFieldVariable
        {
            [Test]
            public void FindFieldVariable_AssignedFromContextRequire_ReturnsLocalName()
            {
                const string source = "_health = Context.Require<HealthAspect>();";

                var result = AspectUsageAnalyzer.FindFieldVariable(source, "HealthAspect");

                Assert.AreEqual("_health", result);
            }

            [Test]
            public void FindFieldVariable_AssignedFromWorldRequire_ReturnsLocalName()
            {
                const string source = "_time = World.Require<TimeAspect>();";

                var result = AspectUsageAnalyzer.FindFieldVariable(source, "TimeAspect");

                Assert.AreEqual("_time", result);
            }

            [Test]
            public void FindFieldVariable_AspectAttribute_ReturnsFieldName()
            {
                const string source = "[Aspect] private readonly HealthAspect _health = default!;";

                var result = AspectUsageAnalyzer.FindFieldVariable(source, "HealthAspect");

                Assert.AreEqual("_health", result);
            }

            [Test]
            public void FindFieldVariable_AspectMissing_ReturnsNull()
            {
                const string source = "_x = Context.Require<OtherAspect>();";

                var result = AspectUsageAnalyzer.FindFieldVariable(source, "HealthAspect");

                Assert.IsNull(result);
            }
        }

        [TestFixture]
        public class AnalyzeFieldUsage
        {
            [Test]
            public void AnalyzeFieldUsage_ValueAssignment_IsWrite()
            {
                const string source = "_aspect.Health.Value = 5;";

                AspectUsageAnalyzer.AnalyzeFieldUsage(source, "_aspect", "Health", out bool read, out bool write);

                Assert.IsTrue(write);
                Assert.IsFalse(read);
            }

            [Test]
            public void AnalyzeFieldUsage_ValueRead_IsRead()
            {
                const string source = "var v = _aspect.Health.Value;";

                AspectUsageAnalyzer.AnalyzeFieldUsage(source, "_aspect", "Health", out bool read, out bool write);

                Assert.IsTrue(read);
                Assert.IsFalse(write);
            }

            [Test]
            public void AnalyzeFieldUsage_OnNextCall_IsWrite()
            {
                const string source = "_aspect.Health.OnNext(5);";

                AspectUsageAnalyzer.AnalyzeFieldUsage(source, "_aspect", "Health", out bool read, out bool write);

                Assert.IsTrue(write);
            }

            [Test]
            public void AnalyzeFieldUsage_SubscribeCall_IsRead()
            {
                const string source = "_aspect.Health.Subscribe(x => {});";

                AspectUsageAnalyzer.AnalyzeFieldUsage(source, "_aspect", "Health", out bool read, out bool write);

                Assert.IsTrue(read);
                Assert.IsFalse(write);
            }

            [Test]
            public void AnalyzeFieldUsage_SiblingPrefixField_NoPhantomBinding()
            {
                const string source = "_aspect.HealthPoints.Value = 5;";

                AspectUsageAnalyzer.AnalyzeFieldUsage(source, "_aspect", "Health", out bool read, out bool write);

                Assert.IsFalse(read, "Health must not be matched as a substring of HealthPoints");
                Assert.IsFalse(write, "Health must not be matched as a substring of HealthPoints");
            }

            [Test]
            public void AnalyzeFieldUsage_SiblingPrefixField_ExactFieldStillMatches()
            {
                const string source = "_aspect.HealthPoints.Value = 5;";

                AspectUsageAnalyzer.AnalyzeFieldUsage(source, "_aspect", "HealthPoints", out bool read, out bool write);

                Assert.IsTrue(write);
            }

            [Test]
            public void AnalyzeFieldUsage_ChainedNestedAssignment_NotWriteOnParent()
            {
                const string source = "_aspect.Position.Local.x = 5;";

                AspectUsageAnalyzer.AnalyzeFieldUsage(source, "_aspect", "Position", out bool read, out bool write);

                Assert.IsFalse(write, "Nested assignment .Local.x = 5 must not flag Position as Write");
            }

            [Test]
            public void AnalyzeFieldUsage_DirectFieldAssignment_IsWrite()
            {
                const string source = "_aspect.Position = newPos;";

                AspectUsageAnalyzer.AnalyzeFieldUsage(source, "_aspect", "Position", out bool read, out bool write);

                Assert.IsTrue(write);
            }

            [Test]
            public void AnalyzeFieldUsage_EqualityComparison_IsReadNotWrite()
            {
                const string source = "if (_aspect.Health == 0) {}";

                AspectUsageAnalyzer.AnalyzeFieldUsage(source, "_aspect", "Health", out bool read, out bool write);

                Assert.IsTrue(read);
                Assert.IsFalse(write);
            }

            [Test]
            public void AnalyzeFieldUsage_PrefixedVariable_NoPhantomBinding()
            {
                const string source = "_aspectOther.Health.Value = 5;";

                AspectUsageAnalyzer.AnalyzeFieldUsage(source, "_aspect", "Health", out bool read, out bool write);

                Assert.IsFalse(read, "_aspect must not be matched as a prefix of _aspectOther");
                Assert.IsFalse(write, "_aspect must not be matched as a prefix of _aspectOther");
            }

            [Test]
            public void AnalyzeFieldUsage_CompoundAssignment_IsWrite()
            {
                const string source = "_aspect.Health.Value += 1;";

                AspectUsageAnalyzer.AnalyzeFieldUsage(source, "_aspect", "Health", out bool read, out bool write);

                Assert.IsTrue(write);
            }
        }
    }
}
