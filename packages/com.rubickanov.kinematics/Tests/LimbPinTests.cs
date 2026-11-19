using NUnit.Framework;
using Unity.Mathematics;

namespace Rubickanov.Kinematics.Tests
{
    public sealed class LimbPinTests
    {
        private const double Dt = 1.0 / 60.0;
        private static readonly double3 Up = new(0, 1, 0);
        private static readonly LimbPinSettings Settings = new(0.04, 0.04, 0.2, 0.25, 0.08, 0.15);

        [Test]
        public void HoldsAPlantedContactWhereItTouchedDown()
        {
            var pin = new LimbPin();
            pin.Update(double3.zero, 0.0, Up, Settings, Dt);
            var contact = new double3(0.1, 0, 0.05);
            double3 shift = pin.Update(contact, 0.0, Up, Settings, Dt);

            Assert.IsTrue(pin.IsPlanted);
            AssertNear(double3.zero, contact + shift);
        }

        [Test]
        public void HoldsOnlyAcrossTheGround()
        {
            var pin = new LimbPin();
            pin.Update(double3.zero, 0.0, Up, Settings, Dt);
            double3 shift = pin.Update(new double3(0.1, 0.03, 0), 0.03, Up, Settings, Dt);

            AssertNear(new double3(-0.1, 0, 0), shift);
        }

        [Test]
        public void LetsGoOnceLiftedAndCatchesUpInTheAir()
        {
            var pin = new LimbPin();
            pin.Update(double3.zero, 0.0, Up, Settings, Dt);
            pin.Update(new double3(0.1, 0, 0), 0.0, Up, Settings, Dt);
            var lifted = new double3(0.1, 0.2, 0);
            double3 shift = pin.Update(lifted, 0.2, Up, Settings, Dt);

            Assert.IsFalse(pin.IsPlanted);
            Assert.Less(DetMath.Length(shift), 0.1);
            for (var i = 0; i < 120; i++)
            {
                shift = pin.Update(lifted, 0.2, Up, Settings, Dt);
            }

            Assert.Less(DetMath.Length(shift), 1e-4);
        }

        [Test]
        public void StaysPlantedWithinTheLiftMargin()
        {
            var pin = new LimbPin();
            pin.Update(double3.zero, 0.0, Up, Settings, Dt);
            pin.Update(new double3(0, 0.06, 0), 0.06, Up, Settings, Dt);

            Assert.IsTrue(pin.IsPlanted);
        }

        [Test]
        public void StepsAfterAContactDraggedTooFarAndPlantsAgainOnTheAnimation()
        {
            var pin = new LimbPin();
            pin.Update(double3.zero, 0.0, Up, Settings, Dt);
            var dragged = new double3(0.3, 0, 0);
            pin.Update(dragged, 0.0, Up, Settings, Dt);
            Assert.IsTrue(pin.IsStepping);

            double highest = 0.0;
            double3 shift = double3.zero;
            for (var i = 0; i < 30; i++)
            {
                shift = pin.Update(dragged, 0.0, Up, Settings, Dt);
                highest = math.max(highest, shift.y);
            }

            Assert.AreEqual(Settings.StepHeight, highest, 0.01);
            Assert.IsTrue(pin.IsPlanted);
            AssertNear(double3.zero, shift);
        }

        [Test]
        public void KeepsWhatIsShownWhenTheHeldPointChangesBone()
        {
            var pin = new LimbPin();
            pin.Update(double3.zero, 0.0, Up, Settings, Dt);
            var heel = new double3(0.05, 0, 0);
            double3 before = heel + pin.Update(heel, 0.0, Up, Settings, Dt);

            var toes = new double3(0.2, 0, 0);
            pin.Rebase(heel, toes, Up);
            double3 toesShift = pin.Update(toes, 0.0, Up, Settings, Dt);

            AssertNear(before - heel, toesShift);
        }

        [Test]
        public void KeepsAStepGoingWhenTheHeldPointChangesBone()
        {
            var pin = new LimbPin();
            pin.Update(double3.zero, 0.0, Up, Settings, Dt);
            var heel = new double3(0.3, 0, 0);
            double3 before = heel + pin.Update(heel, 0.0, Up, Settings, Dt);
            Assert.IsTrue(pin.IsStepping);

            var toes = new double3(0.4, 0, 0);
            pin.Rebase(heel, toes, Up);
            double3 after = heel + pin.Update(toes, 0.0, Up, Settings, 0.0);

            AssertNear(before, after);
        }

        [Test]
        public void ReleasedFarFromTheAnimationStepsBackAndNeverPlantsAgain()
        {
            var pin = new LimbPin();
            pin.Update(double3.zero, 0.0, Up, Settings, Dt);
            var contact = new double3(0.15, 0, 0);
            pin.Update(contact, 0.0, Up, Settings, Dt);
            pin.Release(Settings, contact, Up);

            Assert.IsTrue(pin.IsStepping);
            double3 shift = double3.zero;
            for (var i = 0; i < 60; i++)
            {
                shift = pin.Update(contact, 0.0, Up, Settings, Dt);
            }

            Assert.IsFalse(pin.IsPlanted);
            AssertNear(double3.zero, shift);
        }

        private static void AssertNear(double3 expected, double3 actual) =>
            Assert.Less(DetMath.Distance(expected, actual), 1e-4, $"expected {expected}, got {actual}");
    }
}
