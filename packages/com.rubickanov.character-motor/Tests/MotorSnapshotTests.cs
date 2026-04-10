using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class MotorSnapshotTests
    {
        [Test]
        public void Ctor_StateWithVerticalVelocity_HorizontalSpeedExcludesY()
        {
            var state = new MotorState
            {
                CurrentVelocity = new Vector3(3f, 10f, 4f),
            };

            var snapshot = new MotorSnapshot(state);

            Assert.AreEqual(5f, snapshot.HorizontalSpeed, 0.0001f);
            Assert.AreEqual(new Vector3(3f, 10f, 4f), snapshot.Velocity);
        }

        [Test]
        public void Ctor_ZeroVelocity_HorizontalSpeedZero()
        {
            var state = new MotorState();

            var snapshot = new MotorSnapshot(state);

            Assert.AreEqual(0f, snapshot.HorizontalSpeed);
        }

        [Test]
        public void Ctor_CopiesFlagsAndGroundInfo_FromState()
        {
            var state = new MotorState
            {
                IsGrounded = true,
                IsSprinting = true,
                IsCrouching = true,
                IsSliding = true,
                GroundNormal = new Vector3(0f, 0.707f, 0.707f),
                GroundAngle = 45f,
            };

            var snapshot = new MotorSnapshot(state);

            Assert.IsTrue(snapshot.IsGrounded);
            Assert.IsTrue(snapshot.IsSprinting);
            Assert.IsTrue(snapshot.IsCrouching);
            Assert.IsTrue(snapshot.IsSliding);
            Assert.AreEqual(new Vector3(0f, 0.707f, 0.707f), snapshot.GroundNormal);
            Assert.AreEqual(45f, snapshot.GroundAngle);
        }
    }
}
