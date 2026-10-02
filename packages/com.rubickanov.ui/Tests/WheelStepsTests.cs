using NUnit.Framework;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class WheelStepsTests
    {
        [Test]
        public void Take_ANotch_OneStepTowardsItsSign()
        {
            var wheel = new WheelSteps();

            var down = wheel.Take(WheelSteps.DefaultNotch, out var downDirection);
            var up = wheel.Take(-WheelSteps.DefaultNotch, out var upDirection);

            Assert.IsTrue(down);
            Assert.AreEqual(1, downDirection);
            Assert.IsTrue(up);
            Assert.AreEqual(-1, upDirection);
        }

        [Test]
        public void Take_EmptyOrSidewaysEvent_NoStep()
        {
            Assert.IsFalse(new WheelSteps().Take(0f, out _));
        }

        [Test]
        public void Take_TouchpadsManySmallEvents_OneStepPerNotchWorth()
        {
            var wheel = new WheelSteps();
            var steps = 0;

            for (var i = 0; i < 60; i++)
            {
                if (wheel.Take(0.25f, out _)) steps++;
            }

            Assert.AreEqual(5, steps);
        }

        [Test]
        public void Take_TurnBack_DropsWhatWasGathered()
        {
            var wheel = new WheelSteps();

            Assert.IsFalse(wheel.Take(2f, out _));
            Assert.IsFalse(wheel.Take(-2f, out _));
            Assert.IsTrue(wheel.Take(-1f, out var direction));
            Assert.AreEqual(-1, direction);
        }

        [Test]
        public void Reset_PartlyTurned_StartsOver()
        {
            var wheel = new WheelSteps();
            wheel.Take(2f, out _);

            wheel.Reset();

            Assert.IsFalse(wheel.Take(2f, out _));
        }
    }
}
