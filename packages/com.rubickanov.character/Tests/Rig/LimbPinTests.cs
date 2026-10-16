using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Rig.Tests
{
    public sealed class LimbPinTests
    {
        private const float Dt = 1f / 60f;
        private static readonly Vector3 Up = Vector3.up;
        private static readonly LimbPinSettings Settings = new(0.04f, 0.04f, 0.2f, 0.25f, 0.08f, 0.15f);

        [Test]
        public void HoldsAPlantedContactWhereItTouchedDown()
        {
            var pin = new LimbPin();
            pin.Update(Vector3.zero, 0f, Up, Settings, Dt);
            Vector3 contact = new(0.1f, 0f, 0.05f);
            Vector3 shift = pin.Update(contact, 0f, Up, Settings, Dt);

            Assert.IsTrue(pin.IsPlanted);
            AssertNear(Vector3.zero, contact + shift);
        }

        [Test]
        public void HoldsOnlyAcrossTheGround()
        {
            var pin = new LimbPin();
            pin.Update(Vector3.zero, 0f, Up, Settings, Dt);
            Vector3 shift = pin.Update(new Vector3(0.1f, 0.03f, 0f), 0.03f, Up, Settings, Dt);

            AssertNear(new Vector3(-0.1f, 0f, 0f), shift);
        }

        [Test]
        public void LetsGoOnceLiftedAndCatchesUpInTheAir()
        {
            var pin = new LimbPin();
            pin.Update(Vector3.zero, 0f, Up, Settings, Dt);
            pin.Update(new Vector3(0.1f, 0f, 0f), 0f, Up, Settings, Dt);
            Vector3 lifted = new(0.1f, 0.2f, 0f);
            Vector3 shift = pin.Update(lifted, 0.2f, Up, Settings, Dt);

            Assert.IsFalse(pin.IsPlanted);
            Assert.Less(shift.magnitude, 0.1f);
            for (int i = 0; i < 120; i++)
            {
                shift = pin.Update(lifted, 0.2f, Up, Settings, Dt);
            }

            Assert.Less(shift.magnitude, 1e-4f);
        }

        [Test]
        public void StaysPlantedWithinTheLiftMargin()
        {
            var pin = new LimbPin();
            pin.Update(Vector3.zero, 0f, Up, Settings, Dt);
            pin.Update(new Vector3(0f, 0.06f, 0f), 0.06f, Up, Settings, Dt);

            Assert.IsTrue(pin.IsPlanted);
        }

        [Test]
        public void StepsAfterAContactDraggedTooFarAndPlantsAgainOnTheAnimation()
        {
            var pin = new LimbPin();
            pin.Update(Vector3.zero, 0f, Up, Settings, Dt);
            Vector3 dragged = new(0.3f, 0f, 0f);
            pin.Update(dragged, 0f, Up, Settings, Dt);
            Assert.IsTrue(pin.IsStepping);

            float highest = 0f;
            Vector3 shift = Vector3.zero;
            for (int i = 0; i < 30; i++)
            {
                shift = pin.Update(dragged, 0f, Up, Settings, Dt);
                highest = Mathf.Max(highest, shift.y);
            }

            Assert.AreEqual(Settings.StepHeight, highest, 0.01f);
            Assert.IsTrue(pin.IsPlanted);
            AssertNear(Vector3.zero, shift);
        }

        [Test]
        public void KeepsWhatIsShownWhenTheHeldPointChangesBone()
        {
            var pin = new LimbPin();
            pin.Update(Vector3.zero, 0f, Up, Settings, Dt);
            Vector3 heel = new(0.05f, 0f, 0f);
            Vector3 before = heel + pin.Update(heel, 0f, Up, Settings, Dt);

            Vector3 toes = new(0.2f, 0f, 0f);
            pin.Rebase(heel, toes, Up);
            Vector3 toesShift = pin.Update(toes, 0f, Up, Settings, Dt);

            AssertNear(before - heel, toesShift);
        }

        [Test]
        public void KeepsAStepGoingWhenTheHeldPointChangesBone()
        {
            var pin = new LimbPin();
            pin.Update(Vector3.zero, 0f, Up, Settings, Dt);
            Vector3 heel = new(0.3f, 0f, 0f);
            Vector3 before = heel + pin.Update(heel, 0f, Up, Settings, Dt);
            Assert.IsTrue(pin.IsStepping);

            Vector3 toes = new(0.4f, 0f, 0f);
            pin.Rebase(heel, toes, Up);
            Vector3 after = heel + pin.Update(toes, 0f, Up, Settings, 0f);

            AssertNear(before, after);
        }

        [Test]
        public void ReleasedFarFromTheAnimationStepsBackAndNeverPlantsAgain()
        {
            var pin = new LimbPin();
            pin.Update(Vector3.zero, 0f, Up, Settings, Dt);
            Vector3 contact = new(0.15f, 0f, 0f);
            pin.Update(contact, 0f, Up, Settings, Dt);
            pin.Release(Settings, contact, Up);

            Assert.IsTrue(pin.IsStepping);
            Vector3 shift = Vector3.zero;
            for (int i = 0; i < 60; i++)
            {
                shift = pin.Update(contact, 0f, Up, Settings, Dt);
            }

            Assert.IsFalse(pin.IsPlanted);
            AssertNear(Vector3.zero, shift);
        }

        private static void AssertNear(Vector3 expected, Vector3 actual) =>
            Assert.Less(Vector3.Distance(expected, actual), 1e-4f, $"expected {expected}, got {actual}");
    }
}
