using NUnit.Framework;
using Unity.Mathematics;

namespace Rubickanov.Kinematics.Tests
{
    public sealed class HeadLookTests
    {
        // A ragdoll's neck: lower neck turns 40° each way, bends 35° down and 45° up; the head 40°, 15° and 25°.
        private static readonly LookRange Lower = new(40, 40, 35, 45);
        private static readonly LookRange Upper = new(40, 40, 15, 25);
        private static readonly double3 Eyes = new(0, 1.65, 0.1);
        private static readonly double3 Forward = new(0, 0, 1);

        [Test]
        public void LooksStraightAtATargetWithinReach()
        {
            var target = new double3(1.0, 2.1, 3.0);
            NeckPose neck = HeadLook.Toward(Rotation.Identity, Eyes, target, Lower, Upper);

            double3 look = Rotation.Rotate(Rotation.Mul(neck.Lower, neck.Upper), Forward);
            Assert.Less(DetMath.Distance(look, DetMath.Normalize(target - Eyes)), 1e-9);
        }

        [Test]
        public void ReadsTheLookInTheChestsAxes()
        {
            double4 chest = Rotation.AxisAngle(new double3(0, 1, 0), DetMath.HalfPi);
            NeckPose neck = HeadLook.Toward(chest, Eyes, Eyes + new double3(5, 0, 0), Lower, Upper);

            Assert.AreEqual(0.0, neck.Aim.Yaw, 1e-9);
            Assert.AreEqual(0.0, neck.LowerTurn, 1e-9);
            Assert.AreEqual(0.0, neck.UpperFlexion, 1e-9);
        }

        [Test]
        public void SharesTheLookByRange()
        {
            NeckPose right = HeadLook.Solve(new AimAngles(30, 0), Lower, Upper);
            Assert.AreEqual(15.0, right.LowerTurn, 1e-9);
            Assert.AreEqual(15.0, right.UpperTurn, 1e-9);
            Assert.AreEqual(0.0, right.LowerFlexion, 1e-9);
            Assert.AreEqual(0.0, right.UpperFlexion, 1e-9);

            NeckPose down = HeadLook.Solve(new AimAngles(0, -40), Lower, Upper);
            Assert.AreEqual(28.0, down.LowerFlexion, 1e-9);
            Assert.AreEqual(12.0, down.UpperFlexion, 1e-9);
        }

        [Test]
        public void BendsDownAndTurnsRightWithPositiveAngles()
        {
            NeckPose neck = HeadLook.Toward(Rotation.Identity, Eyes, Eyes + new double3(1, -1, 2), Lower, Upper);
            Assert.Greater(neck.LowerFlexion, 0.0);
            Assert.Greater(neck.UpperFlexion, 0.0);
            Assert.Greater(neck.LowerTurn, 0.0);
            Assert.Greater(neck.UpperTurn, 0.0);

            // A bend about X by a positive angle tips the forward axis down; a turn about Y swings it right.
            double3 look = Rotation.Rotate(HeadLook.Joint(20, 30), Forward);
            Assert.Less(look.y, 0.0);
            Assert.Greater(look.x, 0.0);
        }

        [Test]
        public void HoldsAtTheNecksReach()
        {
            NeckPose behind = HeadLook.Toward(Rotation.Identity, Eyes, Eyes + new double3(1, 0, -3), Lower, Upper);
            Assert.AreEqual(80.0, behind.Aim.Yaw, 1e-6);
            Assert.AreEqual(40.0, behind.LowerTurn, 1e-9);
            Assert.AreEqual(40.0, behind.UpperTurn, 1e-9);

            NeckPose up = HeadLook.Solve(new AimAngles(0, 89), Lower, Upper);
            Assert.AreEqual(70.0, up.Aim.Pitch, 1e-6);
            Assert.AreEqual(-45.0, up.LowerFlexion, 1e-9);
            Assert.AreEqual(-25.0, up.UpperFlexion, 1e-6);
        }

        [Test]
        public void KeepsEachJointInItsRange()
        {
            var rng = new System.Random(5);
            for (var i = 0; i < 2000; i++)
            {
                var target = new double3(rng.NextDouble() * 6 - 3, rng.NextDouble() * 6 - 3, rng.NextDouble() * 6 - 3);
                NeckPose neck = HeadLook.Toward(Rotation.Identity, double3.zero, target, Lower, Upper);
                InRange(neck.LowerTurn, -Lower.Left, Lower.Right, i);
                InRange(neck.LowerFlexion, -Lower.Up, Lower.Down, i);
                InRange(neck.UpperTurn, -Upper.Left, Upper.Right, i);
                InRange(neck.UpperFlexion, -Upper.Up, Upper.Down, i);
                Assert.IsFalse(double.IsNaN(neck.Upper.w), $"case {i}");
            }
        }

        private static void InRange(double value, double min, double max, int i)
        {
            Assert.GreaterOrEqual(value, min - 1e-9, $"case {i}");
            Assert.LessOrEqual(value, max + 1e-9, $"case {i}");
        }
    }
}
