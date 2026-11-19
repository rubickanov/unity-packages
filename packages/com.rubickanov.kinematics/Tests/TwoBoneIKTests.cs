using NUnit.Framework;
using Unity.Mathematics;

namespace Rubickanov.Kinematics.Tests
{
    public sealed class TwoBoneIKTests
    {
        private static readonly double3 Forward = new(0, 0, 1);

        // A slightly bent leg: hip at 1 m, knee forward, foot on the ground.
        private static TwoBoneChain Leg() => new(new double3(0, 1, 0), new double3(0, 0.5, 0.05), double3.zero,
            Rotation.Identity, Rotation.Identity, Rotation.Identity);

        [Test]
        public void ReachesTargetAndKeepsLengths()
        {
            TwoBoneChain leg = Leg();
            var target = new double3(0.1, 0.3, 0.1);

            TwoBoneChain solved = TwoBoneIK.Solve(leg, target, leg.LowerPosition + Forward, 1.0, 0.0);

            Assert.Less(DetMath.Distance(solved.EndPosition, target), 1e-3);
            Assert.AreEqual(UpperLength(leg), UpperLength(solved), 1e-4);
            Assert.AreEqual(LowerLength(leg), LowerLength(solved), 1e-4);
        }

        [Test]
        public void KneePointsTowardHint()
        {
            var target = new double3(0, 0.4, 0);
            var hint = new double3(1, 0.7, 0);

            TwoBoneChain solved = TwoBoneIK.Solve(Leg(), target, hint, 1.0, 0.0);

            Assert.Greater(solved.LowerPosition.x, 0.1);
            Assert.AreEqual(0.0, solved.LowerPosition.z, 1e-3);
        }

        [Test]
        public void OutOfReachStraightensWithoutNaN()
        {
            TwoBoneChain leg = Leg();
            var target = new double3(0, -5, 0);

            TwoBoneChain solved = TwoBoneIK.Solve(leg, target, leg.LowerPosition + Forward, 1.0);

            Assert.IsFalse(double.IsNaN(solved.EndPosition.y));
            double3 toEnd = DetMath.Normalize(solved.EndPosition - solved.UpperPosition);
            Assert.Greater(math.dot(toEnd, new double3(0, -1, 0)), 0.999);
        }

        [Test]
        public void KeepsEndRotation()
        {
            TwoBoneChain leg = Leg();
            leg.EndRotation = Rotation.AxisAngle(new double3(0.3, 1, -0.2), 0.7);

            TwoBoneChain solved = TwoBoneIK.Solve(leg, new double3(0.2, 0.2, 0), leg.LowerPosition + Forward, 1.0);

            Assert.Less(Rotation.Angle(leg.EndRotation, solved.EndRotation) * DetMath.Rad2Deg, 1e-3);
        }

        [Test]
        public void ZeroWeightLeavesPose()
        {
            TwoBoneChain leg = Leg();

            TwoBoneChain solved = TwoBoneIK.Solve(leg, new double3(0.3, 0.3, 0), Forward, 0.0);

            Assert.AreEqual(leg.EndPosition, solved.EndPosition);
        }

        [Test]
        public void RotationsCarryTheBones()
        {
            TwoBoneChain leg = Leg();
            foreach (double weight in new[] { 1.0, 0.5 })
            {
                TwoBoneChain solved = TwoBoneIK.Solve(leg, new double3(0.15, 0.35, 0.2), new double3(0.3, 0.6, 1),
                    weight);

                double3 knee = leg.UpperPosition + Rotation.Rotate(
                    Rotation.Mul(solved.UpperRotation, Rotation.Inverse(leg.UpperRotation)),
                    leg.LowerPosition - leg.UpperPosition);
                double3 foot = knee + Rotation.Rotate(
                    Rotation.Mul(solved.LowerRotation, Rotation.Inverse(leg.LowerRotation)),
                    leg.EndPosition - leg.LowerPosition);
                Assert.Less(DetMath.Distance(knee, solved.LowerPosition), 1e-9, $"weight {weight}");
                Assert.Less(DetMath.Distance(foot, solved.EndPosition), 1e-9, $"weight {weight}");
                Assert.AreEqual(UpperLength(leg), UpperLength(solved), 1e-9);
                Assert.AreEqual(LowerLength(leg), LowerLength(solved), 1e-9);
            }
        }

        [Test]
        public void SoftDistanceNeverExceedsLength()
        {
            Assert.AreEqual(0.5, TwoBoneIK.SoftDistance(0.5, 1.0, 0.05), 1e-6);
            Assert.Less(TwoBoneIK.SoftDistance(1.0, 1.0, 0.05), 1.0);
            Assert.LessOrEqual(TwoBoneIK.SoftDistance(10.0, 1.0, 0.05), 1.0);
        }

        private static double UpperLength(in TwoBoneChain c) => DetMath.Distance(c.UpperPosition, c.LowerPosition);

        private static double LowerLength(in TwoBoneChain c) => DetMath.Distance(c.LowerPosition, c.EndPosition);
    }
}
