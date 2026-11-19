using NUnit.Framework;
using Unity.Mathematics;

namespace Rubickanov.Kinematics.Tests
{
    public sealed class AimAnglesTests
    {
        [Test]
        public void ReadsYawAndPitch()
        {
            // Unity's Quaternion.Euler(-30, 40, 0): the yaw about Y after the pitch about X.
            double4 turn = Rotation.Mul(Rotation.AxisAngle(new double3(0, 1, 0), 40 * DetMath.Deg2Rad),
                Rotation.AxisAngle(new double3(1, 0, 0), -30 * DetMath.Deg2Rad));
            AimAngles angles = AimAngles.FromLocal(Rotation.Rotate(turn, new double3(0, 0, 1)));
            Assert.AreEqual(40.0, angles.Yaw, 1e-3);
            Assert.AreEqual(30.0, angles.Pitch, 1e-3);
        }

        [Test]
        public void DirectionReadsBackItsAngles()
        {
            AimAngles angles = AimAngles.FromLocal(new AimAngles(-125.0, 61.0).Direction());
            Assert.AreEqual(-125.0, angles.Yaw, 1e-9);
            Assert.AreEqual(61.0, angles.Pitch, 1e-9);
        }

        [Test]
        public void KeepsAnglesWithinLimits()
        {
            AimAngles angles = new AimAngles(-50.0, 20.0).Limit(75.0, 45.0, 60.0);
            Assert.AreEqual(-50.0, angles.Yaw, 1e-4);
            Assert.AreEqual(20.0, angles.Pitch, 1e-4);
        }

        [Test]
        public void ClampsPitch()
        {
            AimAngles angles = new AimAngles(0.0, -80.0).Limit(75.0, 45.0, 60.0);
            Assert.AreEqual(-60.0, angles.Pitch, 1e-4);
        }

        [Test]
        public void HoldsAtYawLimit()
        {
            AimAngles side = new AimAngles(95.0, 30.0).Limit(75.0, 45.0, 60.0);
            AimAngles behind = new AimAngles(-179.0, 30.0).Limit(75.0, 45.0, 60.0);
            Assert.AreEqual(75.0, side.Yaw, 1e-4);
            Assert.AreEqual(30.0, side.Pitch, 1e-4);
            Assert.AreEqual(-75.0, behind.Yaw, 1e-4);
            Assert.AreEqual(30.0, behind.Pitch, 1e-4);
        }
    }
}
