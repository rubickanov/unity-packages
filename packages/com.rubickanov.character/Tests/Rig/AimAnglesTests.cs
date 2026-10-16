using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Rig.Tests
{
    public sealed class AimAnglesTests
    {
        [Test]
        public void ReadsYawAndPitch()
        {
            AimAngles angles = AimAngles.FromLocal(Quaternion.Euler(-30f, 40f, 0f) * Vector3.forward);
            Assert.AreEqual(40f, angles.Yaw, 1e-3f);
            Assert.AreEqual(30f, angles.Pitch, 1e-3f);
        }

        [Test]
        public void KeepsAnglesWithinLimits()
        {
            AimAngles angles = new AimAngles(-50f, 20f).Limit(75f, 45f, 60f);
            Assert.AreEqual(-50f, angles.Yaw, 1e-4f);
            Assert.AreEqual(20f, angles.Pitch, 1e-4f);
        }

        [Test]
        public void ClampsPitch()
        {
            AimAngles angles = new AimAngles(0f, -80f).Limit(75f, 45f, 60f);
            Assert.AreEqual(-60f, angles.Pitch, 1e-4f);
        }

        [Test]
        public void HoldsAtYawLimit()
        {
            AimAngles side = new AimAngles(95f, 30f).Limit(75f, 45f, 60f);
            AimAngles behind = new AimAngles(-179f, 30f).Limit(75f, 45f, 60f);
            Assert.AreEqual(75f, side.Yaw, 1e-4f);
            Assert.AreEqual(30f, side.Pitch, 1e-4f);
            Assert.AreEqual(-75f, behind.Yaw, 1e-4f);
            Assert.AreEqual(30f, behind.Pitch, 1e-4f);
        }
    }
}
