using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class MotorStateTests
    {
        [Test]
        public void ApplyInput_AllCoreFields_CopiedToState()
        {
            var state = new MotorState();
            var input = new MotorInput
            {
                Move = new Vector2(0.3f, -0.7f),
                Jump = true,
                Sprint = true,
                Crouch = true,
            };

            state.ApplyInput(input);

            Assert.AreEqual(new Vector2(0.3f, -0.7f), state.MoveInput);
            Assert.IsTrue(state.JumpPressed);
            Assert.IsTrue(state.SprintHeld);
            Assert.IsTrue(state.CrouchPressed);
        }

        [Test]
        public void ApplyInput_ExtensionsReference_PropagatedToState()
        {
            var state = new MotorState();
            var extensions = new InputExtensions();
            var input = new MotorInput { Extensions = extensions };

            state.ApplyInput(input);

            Assert.AreSame(extensions, state.InputExtensions);
        }

        [Test]
        public void ApplyInput_NoExtensions_LeavesInputExtensionsNull()
        {
            var state = new MotorState();

            state.ApplyInput(new MotorInput());

            Assert.IsNull(state.InputExtensions);
        }

        [Test]
        public void ResetPerFrame_MultipliersAndFrameFlags_ResetToDefaults()
        {
            var state = new MotorState
            {
                SpeedMultiplier = 2.5f,
                GravityMultiplier = 0.25f,
                ExternalForce = new Vector3(1f, 2f, 3f),
                SkipDefaultPhysics = true,
                IsSliding = true,
                GroundVelocity = new Vector3(5f, 0f, 0f),
            };

            state.ResetPerFrame();

            Assert.AreEqual(1f, state.SpeedMultiplier);
            Assert.AreEqual(1f, state.GravityMultiplier);
            Assert.AreEqual(Vector3.zero, state.ExternalForce);
            Assert.IsFalse(state.SkipDefaultPhysics);
            Assert.IsFalse(state.IsSliding);
            Assert.AreEqual(Vector3.zero, state.GroundVelocity);
        }

        [Test]
        public void ResetPerFrame_PersistentFields_LeftUntouched()
        {
            var state = new MotorState
            {
                IsGrounded = true,
                IsSprinting = true,
                IsCrouching = true,
                IsInAir = true,
                CurrentVelocity = new Vector3(3f, 0f, 4f),
                DesiredVelocity = new Vector3(1f, 0f, 1f),
                GroundNormal = new Vector3(0f, 0.707f, 0.707f),
                GroundAngle = 45f,
            };

            state.ResetPerFrame();

            Assert.IsTrue(state.IsGrounded);
            Assert.IsTrue(state.IsSprinting);
            Assert.IsTrue(state.IsCrouching);
            Assert.IsTrue(state.IsInAir);
            Assert.AreEqual(new Vector3(3f, 0f, 4f), state.CurrentVelocity);
            Assert.AreEqual(new Vector3(1f, 0f, 1f), state.DesiredVelocity);
            Assert.AreEqual(new Vector3(0f, 0.707f, 0.707f), state.GroundNormal);
            Assert.AreEqual(45f, state.GroundAngle);
        }
    }
}
