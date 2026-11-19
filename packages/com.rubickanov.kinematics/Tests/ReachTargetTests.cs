using NUnit.Framework;
using Unity.Mathematics;

namespace Rubickanov.Kinematics.Tests
{
    public sealed class ReachTargetTests
    {
        private const double Dt = 1.0 / 120.0;
        private static readonly double3 Shoulder = new(0.0, 1.4, 0.0);
        private static readonly double3 ClipHand = new(0.2, 1.0, 0.1);

        [Test]
        public void TheHandTravelsNoFasterThanItsSpeedAndStopsWithinTheArm()
        {
            var state = new ReachState();
            ReachSettings settings = ReachSettings.Default;
            var target = new double3(0.0, 1.4, 2.0);
            double3 last = ClipHand;
            var held = false;
            for (var t = 0; t < 240; t++)
            {
                double3 hand = ReachTarget.Step(ref state, settings, ClipHand, Shoulder, 0.6, target, true, Dt);
                // Once the reach holds the whole pose, the hand only travels.
                if (held)
                {
                    Assert.LessOrEqual(math.distance(hand, last), settings.Speed * Dt + 1e-9, $"tick {t}");
                }

                last = hand;
                held = state.Weight >= 1.0;
            }

            Assert.AreEqual(0.6 * settings.Stretch, math.distance(last, Shoulder), 1e-9, "held within the arm");
            Assert.Greater(last.z, 0.5, "out toward the target");
        }

        [Test]
        public void LettingGoGivesTheClipsHandBackAndAFreshReachStartsFromIt()
        {
            var state = new ReachState();
            ReachSettings settings = ReachSettings.Default;
            var target = new double3(0.0, 1.3, 0.5);
            for (var t = 0; t < 120; t++)
            {
                ReachTarget.Step(ref state, settings, ClipHand, Shoulder, 0.6, target, true, Dt);
            }

            double3 hand = default;
            for (var t = 0; t < 60; t++)
            {
                hand = ReachTarget.Step(ref state, settings, ClipHand, Shoulder, 0.6, target, false, Dt);
            }

            Assert.AreEqual(0.0, state.Weight);
            Assert.AreEqual(0.0, math.distance(hand, ClipHand), 1e-12);
            ReachTarget.Step(ref state, settings, ClipHand + 0.1, Shoulder, 0.6, target, true, Dt);
            Assert.LessOrEqual(math.distance(state.Hand, ClipHand + 0.1), settings.Speed * Dt + 1e-9,
                "starts from the clip's hand, not from the last reach");
        }
    }
}
