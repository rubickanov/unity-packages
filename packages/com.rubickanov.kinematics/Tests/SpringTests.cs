using NUnit.Framework;

namespace Rubickanov.Kinematics.Tests
{
    public sealed class SpringTests
    {
        private static double Run(SpringSettings settings, double deltaTime, double seconds, out double peak)
        {
            var spring = new ScalarSpring();
            peak = 0.0;
            for (double t = 0.0; t < seconds; t += deltaTime)
            {
                peak = System.Math.Max(peak, spring.Step(1.0, settings, deltaTime));
            }

            return spring.Value;
        }

        [TestCase(1.0)]
        [TestCase(0.4)]
        [TestCase(2.0)]
        public void SettlesOnTarget(double damping)
        {
            double value = Run(new SpringSettings(3.0, damping), 1.0 / 60.0, 5.0, out _);
            Assert.AreEqual(1.0, value, 1e-3);
        }

        [Test]
        public void CriticalDampingNeverOvershoots()
        {
            Run(new SpringSettings(4.0, 1.0), 1.0 / 60.0, 3.0, out double peak);
            Assert.LessOrEqual(peak, 1.0 + 1e-5);
        }

        [Test]
        public void UnderdampingOvershoots()
        {
            Run(new SpringSettings(2.0, 0.4), 1.0 / 60.0, 3.0, out double peak);
            Assert.Greater(peak, 1.05);
        }

        [Test]
        public void StepSizeDoesNotChangeTheResult()
        {
            var settings = new SpringSettings(2.0, 0.5);
            double fine = Run(settings, 1.0 / 240.0, 0.5, out _);
            double coarse = Run(settings, 1.0 / 20.0, 0.5, out _);
            Assert.AreEqual(fine, coarse, 0.02);
        }

        [Test]
        public void HugeStepStaysStable()
        {
            var spring = new ScalarSpring();
            double value = spring.Step(1.0, new SpringSettings(10.0, 0.3), 5.0);
            Assert.AreEqual(1.0, value, 1e-3);
        }

        [Test]
        public void ZeroFrequencySnaps()
        {
            var spring = new ScalarSpring();
            Assert.AreEqual(1.0, spring.Step(1.0, new SpringSettings(0.0, 1.0), 1.0 / 60.0));
        }

        [Test]
        public void AngleTakesTheShortWay()
        {
            var spring = new ScalarSpring();
            spring.Reset(170.0);
            spring.StepAngle(-170.0, new SpringSettings(3.0, 1.0), 1.0 / 60.0);
            Assert.Greater(spring.Value, 170.0);
        }
    }
}
