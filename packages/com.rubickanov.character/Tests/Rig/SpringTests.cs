using NUnit.Framework;

namespace Rubickanov.Rig.Tests
{
    public sealed class SpringTests
    {
        private static float Run(SpringSettings settings, float deltaTime, float seconds, out float peak)
        {
            var spring = new FloatSpring();
            peak = 0f;
            for (float t = 0f; t < seconds; t += deltaTime)
            {
                peak = System.Math.Max(peak, spring.Step(1f, settings, deltaTime));
            }

            return spring.Value;
        }

        [TestCase(1f)]
        [TestCase(0.4f)]
        [TestCase(2f)]
        public void SettlesOnTarget(float damping)
        {
            float value = Run(new SpringSettings(3f, damping), 1f / 60f, 5f, out _);
            Assert.AreEqual(1f, value, 1e-3f);
        }

        [Test]
        public void CriticalDampingNeverOvershoots()
        {
            Run(new SpringSettings(4f, 1f), 1f / 60f, 3f, out float peak);
            Assert.LessOrEqual(peak, 1f + 1e-5f);
        }

        [Test]
        public void UnderdampingOvershoots()
        {
            Run(new SpringSettings(2f, 0.4f), 1f / 60f, 3f, out float peak);
            Assert.Greater(peak, 1.05f);
        }

        [Test]
        public void StepSizeDoesNotChangeTheResult()
        {
            var settings = new SpringSettings(2f, 0.5f);
            float fine = Run(settings, 1f / 240f, 0.5f, out _);
            float coarse = Run(settings, 1f / 20f, 0.5f, out _);
            Assert.AreEqual(fine, coarse, 0.02f);
        }

        [Test]
        public void HugeStepStaysStable()
        {
            var spring = new FloatSpring();
            float value = spring.Step(1f, new SpringSettings(10f, 0.3f), 5f);
            Assert.AreEqual(1f, value, 1e-3f);
        }

        [Test]
        public void ZeroFrequencySnaps()
        {
            var spring = new FloatSpring();
            Assert.AreEqual(1f, spring.Step(1f, new SpringSettings(0f, 1f), 1f / 60f));
        }

        [Test]
        public void AngleTakesTheShortWay()
        {
            var spring = new FloatSpring();
            spring.Reset(170f);
            spring.StepAngle(-170f, new SpringSettings(3f, 1f), 1f / 60f);
            Assert.Greater(spring.Value, 170f);
        }
    }
}
