using NUnit.Framework;
using Rubickanov.Motor.Modules;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class JumpModuleTests
    {
        private const float JumpForce = 7.5f;
        private const float Dt = 0.02f;

        private MotorState _state = default!;
        private FakeMotorBody _body = default!;
        private JumpModule _module = default!;

        [SetUp]
        public void SetUp()
        {
            _state = new MotorState();
            _body = new FakeMotorBody();
            _module = new JumpModule();
            _module.Initialize(_state, _body, new NullModuleResolver());
        }

        [TearDown]
        public void TearDown()
        {
            _body.Dispose();
        }

        [Test]
        public void Simulate_GroundedAndJumpPressed_AppliesVerticalImpulseEqualToJumpForce()
        {
            _state.IsGrounded = true;
            _state.JumpPressed = true;
            _body.Velocity = Vector3.zero;

            _module.Simulate(Dt);

            Assert.AreEqual(1, _body.ForcesAdded.Count);
            Assert.AreEqual(ForceMode.VelocityChange, _body.ForcesAdded[0].mode);
            Assert.AreEqual(new Vector3(0f, JumpForce, 0f), _body.ForcesAdded[0].force);
        }

        [Test]
        public void Simulate_JumpWhileFalling_ImpulseCountersCurrentVerticalVelocity()
        {
            _state.IsGrounded = true;
            _state.JumpPressed = true;
            _body.Velocity = new Vector3(0f, -2f, 0f);

            _module.Simulate(Dt);

            Assert.AreEqual(new Vector3(0f, JumpForce - (-2f), 0f), _body.ForcesAdded[0].force);
        }

        [Test]
        public void Simulate_CoyoteJump_WithinCoyoteWindowAfterLeavingGround_JumpFires()
        {
            _state.IsGrounded = true;
            _module.Simulate(Dt);

            _state.IsGrounded = false;
            _state.JumpPressed = true;
            _module.Simulate(Dt);

            Assert.AreEqual(1, _body.ForcesAdded.Count);
        }

        [Test]
        public void Simulate_OutsideCoyoteWindow_JumpSuppressed()
        {
            _state.IsGrounded = true;
            _module.Simulate(Dt);

            _state.IsGrounded = false;
            for (int i = 0; i < 10; i++)
                _module.Simulate(Dt);

            _state.JumpPressed = true;
            _module.Simulate(Dt);

            Assert.AreEqual(0, _body.ForcesAdded.Count);
        }

        [Test]
        public void Simulate_JumpPressedBeforeLanding_BufferedJumpFiresWhenGrounded()
        {
            _state.IsGrounded = false;
            _state.JumpPressed = true;
            _module.Simulate(Dt);
            _state.JumpPressed = false;

            _module.Simulate(Dt);

            _state.IsGrounded = true;
            _module.Simulate(Dt);

            Assert.AreEqual(1, _body.ForcesAdded.Count);
            Assert.AreEqual(new Vector3(0f, JumpForce, 0f), _body.ForcesAdded[0].force);
        }

        [Test]
        public void Simulate_DoubleJumpAttempt_SecondJumpSuppressedWhileAirborne()
        {
            _state.IsGrounded = true;
            _state.JumpPressed = true;
            _module.Simulate(Dt);
            _state.JumpPressed = false;
            _state.IsGrounded = false;

            _module.Simulate(Dt);
            _state.JumpPressed = true;
            _module.Simulate(Dt);

            Assert.AreEqual(1, _body.ForcesAdded.Count);
        }

        [Test]
        public void Simulate_SuccessfulJump_FiresJumpedEventWithJumpForce()
        {
            float? received = null;
            _module.Jumped += force => received = force;
            _state.IsGrounded = true;
            _state.JumpPressed = true;

            _module.Simulate(Dt);

            Assert.AreEqual(JumpForce, received);
        }

        [Test]
        public void Simulate_AirToGroundTransition_FiresLandedWithPreImpactVelocity()
        {
            Vector3? received = null;
            _module.Landed += v => received = v;
            _state.IsGrounded = false;
            _state.CurrentVelocity = new Vector3(0f, -5f, 0f);
            _module.Simulate(Dt);

            _state.IsGrounded = true;
            _state.CurrentVelocity = Vector3.zero;
            _module.Simulate(Dt);

            Assert.IsTrue(received.HasValue);
            Assert.AreEqual(new Vector3(0f, -5f, 0f), received!.Value);
        }

        [Test]
        public void Simulate_NoTransition_LandedNotFired()
        {
            _state.IsGrounded = true;
            _module.Simulate(Dt);

            int calls = 0;
            _module.Landed += _ => calls++;

            _module.Simulate(Dt);
            _module.Simulate(Dt);

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void SaveRestore_Roundtrip_RestoredCoyoteAllowsJumpAfterExpiration()
        {
            _state.IsGrounded = true;
            _module.Simulate(Dt);

            var writer = new ModuleStateWriter(32);
            _module.SaveState(ref writer);
            var bytes = writer.ToArray();

            _state.IsGrounded = false;
            for (int i = 0; i < 10; i++)
                _module.Simulate(Dt);
            _state.JumpPressed = true;
            _module.Simulate(Dt);
            _state.JumpPressed = false;
            Assert.AreEqual(0, _body.ForcesAdded.Count, "Precondition: coyote expired so jump fails");

            var reader = new ModuleStateReader(bytes);
            _module.RestoreState(ref reader);
            _state.IsGrounded = false;
            _state.JumpPressed = true;
            _module.Simulate(Dt);

            Assert.AreEqual(1, _body.ForcesAdded.Count);
        }
    }
}
