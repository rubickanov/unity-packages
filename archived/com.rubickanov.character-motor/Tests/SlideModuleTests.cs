using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rubickanov.Motor.Modules;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class SlideModuleTests
    {
        private const float Dt = 0.02f;
        private const float MinSlideSpeed = 4f;
        private const float SlideBoost = 1.5f;
        private const float MaxSlideSpeed = 15f;

        private MotorState _state = default!;
        private FakeMotorBody _body = default!;
        private SlideModule _module = default!;

        [SetUp]
        public void SetUp()
        {
            _state = new MotorState();
            _body = new FakeMotorBody();
            _module = new SlideModule();
            _module.Initialize(_state, _body, new NullModuleResolver());
        }

        [TearDown]
        public void TearDown()
        {
            _body.Dispose();
        }

        [Test]
        public void Simulate_SprintAndCrouchAndSpeedAboveMin_EntersSlide()
        {
            ArrangeSlideEntry(horizontalSpeed: MinSlideSpeed + 1f);

            _module.Simulate(Dt);

            Assert.IsTrue(_module.IsSliding);
            Assert.IsFalse(_state.CrouchPressed, "Slide should consume CrouchPressed");
            bool foundEntryBoost = false;
            foreach (var (force, mode) in _body.ForcesAdded)
            {
                if (mode == ForceMode.VelocityChange && Mathf.Approximately(force.x, SlideBoost))
                    foundEntryBoost = true;
            }
            Assert.IsTrue(foundEntryBoost, "Expected entry boost force of slideBoost along slide direction");
        }

        [Test]
        public void Simulate_SpeedBelowMin_DoesNotEnterSlide()
        {
            ArrangeSlideEntry(horizontalSpeed: MinSlideSpeed - 1f);

            _module.Simulate(Dt);

            Assert.IsFalse(_module.IsSliding);
        }

        [Test]
        public void Simulate_CooldownActive_DoesNotEnterSlide()
        {
            SetPrivate("_cooldownTimer", 0.5f);
            ArrangeSlideEntry(horizontalSpeed: MinSlideSpeed + 1f);

            _module.Simulate(Dt);

            Assert.IsFalse(_module.IsSliding);
        }

        [Test]
        public void Simulate_NotSprinting_DoesNotEnterSlide()
        {
            ArrangeSlideEntry(horizontalSpeed: MinSlideSpeed + 1f);
            _state.IsSprinting = false;

            _module.Simulate(Dt);

            Assert.IsFalse(_module.IsSliding);
        }

        [Test]
        public void Simulate_SlidingTick_SetsIsSlidingIsCrouchingAndSkipDefaultPhysics()
        {
            ArrangeSlideEntry(horizontalSpeed: MinSlideSpeed + 1f);
            _module.Simulate(Dt);
            Assert.IsTrue(_module.IsSliding, "Precondition: entered slide");

            _body.ForcesAdded.Clear();
            _module.Simulate(Dt);

            Assert.IsTrue(_state.IsSliding);
            Assert.IsTrue(_state.IsCrouching);
            Assert.IsTrue(_state.SkipDefaultPhysics);
        }

        [Test]
        public void Simulate_Sliding_AppliesFrictionOppositeHorizontalVelocity()
        {
            ArrangeSlideEntry(horizontalSpeed: MinSlideSpeed + 1f);
            _module.Simulate(Dt);

            _body.ForcesAdded.Clear();
            _module.Simulate(Dt);

            bool foundFriction = false;
            foreach (var (force, mode) in _body.ForcesAdded)
            {
                if (mode == ForceMode.Acceleration && force.x < -0.1f)
                    foundFriction = true;
            }
            Assert.IsTrue(foundFriction, "Expected friction force opposing horizontal velocity");
        }

        [Test]
        public void Simulate_SlidingOnDownslope_AppliesSlopeBoostInDownhillDirection()
        {
            _state.IsSprinting = true;
            _state.IsGrounded = true;
            _state.GroundNormal = Quaternion.Euler(30f, 0f, 0f) * Vector3.up;
            _state.GroundAngle = 30f;
            _state.CrouchPressed = true;
            _body.Velocity = new Vector3(0f, 0f, 5f);

            _module.Simulate(Dt);
            Assert.IsTrue(_module.IsSliding, "Precondition: entered slide on slope");

            _body.ForcesAdded.Clear();
            _module.Simulate(Dt);

            bool foundSlopeBoost = false;
            foreach (var (force, mode) in _body.ForcesAdded)
            {
                if (mode == ForceMode.Acceleration && force.z > 0.1f && force.y < -0.1f)
                    foundSlopeBoost = true;
            }
            Assert.IsTrue(foundSlopeBoost, "Expected slope boost force with +Z/-Y components");
        }

        [Test]
        public void Simulate_SlidingAboveMaxSpeed_ClampsVelocity()
        {
            ArrangeSlideEntry(horizontalSpeed: MinSlideSpeed + 1f);
            _module.Simulate(Dt);

            _body.Velocity = new Vector3(MaxSlideSpeed + 5f, 0f, 0f);
            _body.ForcesAdded.Clear();
            _module.Simulate(Dt);

            bool foundClamp = false;
            foreach (var (force, mode) in _body.ForcesAdded)
            {
                if (mode == ForceMode.VelocityChange && Mathf.Approximately(force.x, -5f))
                    foundClamp = true;
            }
            Assert.IsTrue(foundClamp, "Expected clamp impulse of -5 on X (VelocityChange)");
        }

        [Test]
        public void Simulate_SlidingSpeedBelowStop_ExitsSlide()
        {
            ArrangeSlideEntry(horizontalSpeed: MinSlideSpeed + 1f);
            _module.Simulate(Dt);

            _body.Velocity = new Vector3(0.5f, 0f, 0f);
            _module.Simulate(Dt);

            Assert.IsFalse(_module.IsSliding);
        }

        [Test]
        public void Simulate_SlidingJumpPressed_ExitsSlide()
        {
            ArrangeSlideEntry(horizontalSpeed: MinSlideSpeed + 1f);
            _module.Simulate(Dt);

            _state.JumpPressed = true;
            _module.Simulate(Dt);

            Assert.IsFalse(_module.IsSliding);
        }

        [Test]
        public void Simulate_SlidingCrouchPressedAgain_ExitsSlide()
        {
            ArrangeSlideEntry(horizontalSpeed: MinSlideSpeed + 1f);
            _module.Simulate(Dt);

            _state.CrouchPressed = true;
            _module.Simulate(Dt);

            Assert.IsFalse(_module.IsSliding);
        }

        [Test]
        public void Simulate_SlidingLeavesGround_ExitsSlide()
        {
            ArrangeSlideEntry(horizontalSpeed: MinSlideSpeed + 1f);
            _module.Simulate(Dt);

            _state.IsGrounded = false;
            _module.Simulate(Dt);

            Assert.IsFalse(_module.IsSliding);
        }

        [Test]
        public void Simulate_EnterAndExit_FiresSlideChangedTwice()
        {
            var events = new List<bool>();
            _module.SlideChanged += on => events.Add(on);

            ArrangeSlideEntry(horizontalSpeed: MinSlideSpeed + 1f);
            _module.Simulate(Dt);

            _state.JumpPressed = true;
            _module.Simulate(Dt);

            Assert.AreEqual(2, events.Count);
            Assert.IsTrue(events[0]);
            Assert.IsFalse(events[1]);
        }

        [Test]
        public void Simulate_AfterExit_CooldownPreventsImmediateReentry()
        {
            ArrangeSlideEntry(horizontalSpeed: MinSlideSpeed + 1f);
            _module.Simulate(Dt);

            _state.JumpPressed = true;
            _module.Simulate(Dt);
            Assert.IsFalse(_module.IsSliding, "Precondition: exited");

            _state.JumpPressed = false;
            _state.CrouchPressed = true;
            _body.Velocity = new Vector3(MinSlideSpeed + 1f, 0f, 0f);
            _module.Simulate(Dt);

            Assert.IsFalse(_module.IsSliding);
        }

        [Test]
        public void SaveRestore_Roundtrip_RestoresSlidingState()
        {
            ArrangeSlideEntry(horizontalSpeed: MinSlideSpeed + 1f);
            _module.Simulate(Dt);
            Assert.IsTrue(_module.IsSliding, "Precondition: entered slide");

            var writer = new ModuleStateWriter(64);
            _module.SaveState(ref writer);
            var bytes = writer.ToArray();

            _state.JumpPressed = true;
            _module.Simulate(Dt);
            _state.JumpPressed = false;
            Assert.IsFalse(_module.IsSliding, "Precondition: exited before restore");

            var reader = new ModuleStateReader(bytes);
            _module.RestoreState(ref reader);

            Assert.IsTrue(_module.IsSliding);
        }

        private void ArrangeSlideEntry(float horizontalSpeed)
        {
            _state.IsSprinting = true;
            _state.IsGrounded = true;
            _state.GroundNormal = Vector3.up;
            _state.CrouchPressed = true;
            _body.Velocity = new Vector3(horizontalSpeed, 0f, 0f);
        }

        private void SetPrivate(string fieldName, object value)
        {
            var field = typeof(SlideModule).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Private field {fieldName} not found on SlideModule");
            field!.SetValue(_module, value);
        }
    }
}
