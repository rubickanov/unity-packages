using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rubickanov.Motor.Modules;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class SprintModuleTests
    {
        private const float SprintMultiplier = 1.6f;
        private const float Dt = 0.02f;

        private MotorState _state = default!;
        private FakeMotorBody _body = default!;
        private SprintModule _module = default!;

        [SetUp]
        public void SetUp()
        {
            _state = new MotorState();
            _body = new FakeMotorBody();
            _module = new SprintModule();
            _module.Initialize(_state, _body, new NullModuleResolver());
        }

        [TearDown]
        public void TearDown()
        {
            _body.Dispose();
        }

        [Test]
        public void Simulate_SprintHeldGroundedForwardInput_MultipliesSpeedBySprintMultiplier()
        {
            _state.SprintHeld = true;
            _state.IsGrounded = true;
            _state.MoveInput = new Vector2(0f, 1f);

            _module.Simulate(Dt);

            Assert.IsTrue(_state.IsSprinting);
            Assert.AreEqual(SprintMultiplier, _state.SpeedMultiplier, 0.0001f);
        }

        [Test]
        public void Simulate_SprintHeldButAirborne_NoSpeedMultiplier()
        {
            _state.SprintHeld = true;
            _state.IsGrounded = false;
            _state.MoveInput = new Vector2(0f, 1f);

            _module.Simulate(Dt);

            Assert.IsFalse(_state.IsSprinting);
            Assert.AreEqual(1f, _state.SpeedMultiplier, 0.0001f);
        }

        [Test]
        public void Simulate_SprintHeldButCrouching_NoSpeedMultiplier()
        {
            _state.SprintHeld = true;
            _state.IsGrounded = true;
            _state.IsCrouching = true;
            _state.MoveInput = new Vector2(0f, 1f);

            _module.Simulate(Dt);

            Assert.IsFalse(_state.IsSprinting);
            Assert.AreEqual(1f, _state.SpeedMultiplier, 0.0001f);
        }

        [Test]
        public void Simulate_SprintHeldButBackwardInput_NoSpeedMultiplier()
        {
            _state.SprintHeld = true;
            _state.IsGrounded = true;
            _state.MoveInput = new Vector2(0f, -1f);

            _module.Simulate(Dt);

            Assert.IsFalse(_state.IsSprinting);
            Assert.AreEqual(1f, _state.SpeedMultiplier, 0.0001f);
        }

        [Test]
        public void Simulate_SprintStartAndStop_FiresSprintChangedTwice()
        {
            var events = new List<bool>();
            _module.SprintChanged += on => events.Add(on);

            _state.SprintHeld = true;
            _state.IsGrounded = true;
            _state.MoveInput = new Vector2(0f, 1f);
            _module.Simulate(Dt);

            _state.SprintHeld = false;
            _module.Simulate(Dt);

            Assert.AreEqual(2, events.Count);
            Assert.IsTrue(events[0]);
            Assert.IsFalse(events[1]);
        }

        [Test]
        public void Simulate_RampEnabled_ProgressesOverTimeAccordingToCurve()
        {
            SetPrivate("_useRamp", true);
            SetPrivate("_rampUpDuration", 1f);

            _state.SprintHeld = true;
            _state.IsGrounded = true;
            _state.MoveInput = new Vector2(0f, 1f);

            _module.Simulate(0.5f);

            Assert.AreEqual(0.5f, _module.RampProgress, 0.0001f);
            Assert.AreEqual(Mathf.Lerp(1f, SprintMultiplier, 0.5f), _state.SpeedMultiplier, 0.0001f);
        }

        [Test]
        public void Simulate_RampDownAfterRelease_DecreasesProgress()
        {
            SetPrivate("_useRamp", true);
            SetPrivate("_rampUpDuration", 1f);
            SetPrivate("_rampDownDuration", 1f);

            _state.SprintHeld = true;
            _state.IsGrounded = true;
            _state.MoveInput = new Vector2(0f, 1f);
            _module.Simulate(1f);
            Assert.AreEqual(1f, _module.RampProgress, 0.0001f, "Precondition: full ramp progress after 1s sprinting");

            _state.SprintHeld = false;
            _module.Simulate(0.5f);

            Assert.AreEqual(0.5f, _module.RampProgress, 0.0001f);
        }

        [Test]
        public void SaveRestore_Roundtrip_PreservesRampProgress()
        {
            SetPrivate("_useRamp", true);
            SetPrivate("_rampUpDuration", 1f);
            SetPrivate("_rampDownDuration", 1f);

            _state.SprintHeld = true;
            _state.IsGrounded = true;
            _state.MoveInput = new Vector2(0f, 1f);
            _module.Simulate(0.3f);
            float progressBefore = _module.RampProgress;

            var writer = new ModuleStateWriter(16);
            _module.SaveState(ref writer);
            var bytes = writer.ToArray();

            _state.SprintHeld = false;
            _module.Simulate(1f);
            Assert.Less(_module.RampProgress, progressBefore, "Precondition: ramp decreased before restore");

            var reader = new ModuleStateReader(bytes);
            _module.RestoreState(ref reader);

            Assert.AreEqual(progressBefore, _module.RampProgress, 0.0001f);
        }

        private void SetPrivate(string fieldName, object value)
        {
            var field = typeof(SprintModule).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Private field {fieldName} not found on SprintModule");
            field!.SetValue(_module, value);
        }
    }
}
