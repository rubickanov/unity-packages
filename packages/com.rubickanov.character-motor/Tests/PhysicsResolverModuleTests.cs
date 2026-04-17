using System.Reflection;
using NUnit.Framework;
using Rubickanov.Motor.Modules;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class PhysicsResolverModuleTests
    {
        private const float Dt = 0.02f;
        private const float Accel = 80f;
        private const float Decel = 120f;
        private const float AirControlForce = 4f;
        private const float AirControl = 0.3f;
        private const float MaxAirSpeed = 8f;
        private const float AirDrag = 0.5f;
        private const float AirStrafeMaxWishSpeed = 1f;
        private const float Gravity = 28f;
        private const float FallMultiplier = 2.2f;

        private MotorState _state = default!;
        private FakeMotorBody _body = default!;
        private PhysicsResolverModule _module = default!;

        [SetUp]
        public void SetUp()
        {
            _state = new MotorState();
            _body = new FakeMotorBody();
            _module = new PhysicsResolverModule();
            _module.Initialize(_state, _body, new NullModuleResolver());
        }

        [TearDown]
        public void TearDown()
        {
            _body.Dispose();
        }

        [Test]
        public void Simulate_GroundedWithInputAndLargeDiff_ClampsAccelerationToAccelTimesDt()
        {
            _state.IsGrounded = true;
            _state.GroundNormal = Vector3.up;
            _state.MoveInput = new Vector2(1f, 0f);
            _state.DesiredVelocity = new Vector3(10f, 0f, 0f);
            _body.Velocity = Vector3.zero;

            _module.Simulate(Dt);

            Assert.AreEqual(1, _body.ForcesAdded.Count);
            Assert.AreEqual(ForceMode.VelocityChange, _body.ForcesAdded[0].mode);
            Assert.AreEqual(new Vector3(Accel * Dt, 0f, 0f), _body.ForcesAdded[0].force);
        }

        [Test]
        public void Simulate_GroundedNoInputNonZeroVelocity_ClampsByDeceleration()
        {
            _state.IsGrounded = true;
            _state.GroundNormal = Vector3.up;
            _state.MoveInput = Vector2.zero;
            _state.DesiredVelocity = Vector3.zero;
            _body.Velocity = new Vector3(5f, 0f, 0f);

            _module.Simulate(Dt);

            Assert.AreEqual(1, _body.ForcesAdded.Count);
            Assert.AreEqual(new Vector3(-Decel * Dt, 0f, 0f), _body.ForcesAdded[0].force);
        }

        [Test]
        public void Simulate_GroundedInputOpposingVelocity_ClampsByDeceleration()
        {
            _state.IsGrounded = true;
            _state.GroundNormal = Vector3.up;
            _state.MoveInput = new Vector2(-1f, 0f);
            _state.DesiredVelocity = new Vector3(-6f, 0f, 0f);
            _body.Velocity = new Vector3(6f, 0f, 0f);

            _module.Simulate(Dt);

            Assert.AreEqual(1, _body.ForcesAdded.Count);
            Assert.AreEqual(ForceMode.VelocityChange, _body.ForcesAdded[0].mode);
            Assert.AreEqual(new Vector3(-Decel * Dt, 0f, 0f), _body.ForcesAdded[0].force);
        }

        [Test]
        public void Simulate_GroundedOnSlope_ProjectsDesiredVelocityOntoGroundPlane()
        {
            // Slope normal tilts forward: (0, 0.707, 0.707). Projecting desired (0,0,10)
            // onto that plane yields (0, -5, 5) — downhill component drops y below 0.
            _state.IsGrounded = true;
            _state.GroundNormal = Quaternion.Euler(45f, 0f, 0f) * Vector3.up;
            _state.MoveInput = new Vector2(0f, 1f);
            _state.DesiredVelocity = new Vector3(0f, 0f, 10f);
            _body.Velocity = Vector3.zero;

            _module.Simulate(Dt);

            Assert.Less(_body.ForcesAdded[0].force.y, -0.1f);
            Assert.Greater(_body.ForcesAdded[0].force.z, 0.1f);
        }

        [Test]
        public void Simulate_GroundedWithPlatformVelocity_AddsPlatformVelocityToTarget()
        {
            _state.IsGrounded = true;
            _state.GroundNormal = Vector3.up;
            _state.MoveInput = Vector2.zero;
            _state.DesiredVelocity = Vector3.zero;
            _state.GroundVelocity = new Vector3(2f, 0f, 0f);
            _body.Velocity = Vector3.zero;

            _module.Simulate(Dt);

            Assert.AreEqual(1, _body.ForcesAdded.Count);
            Assert.AreEqual(new Vector3(2f, 0f, 0f), _body.ForcesAdded[0].force);
        }

        [Test]
        public void Simulate_AirborneBelowMaxSpeed_AppliesAirControlForce()
        {
            _state.IsGrounded = false;
            _state.DesiredVelocity = new Vector3(1f, 0f, 0f);
            _body.Velocity = Vector3.zero;

            _module.Simulate(Dt);

            Assert.AreEqual(ForceMode.Acceleration, _body.ForcesAdded[0].mode);
            Assert.AreEqual(
                new Vector3(AirControlForce * AirControl, 0f, 0f),
                _body.ForcesAdded[0].force);
        }

        [Test]
        public void Simulate_AirborneAtOrAboveMaxSpeed_NoAirControlForce()
        {
            _state.IsGrounded = false;
            _state.DesiredVelocity = new Vector3(1f, 0f, 0f);
            _body.Velocity = new Vector3(MaxAirSpeed + 2f, 0f, 0f);

            _module.Simulate(Dt);

            foreach (var (force, mode) in _body.ForcesAdded)
                Assert.IsFalse(
                    mode == ForceMode.Acceleration && force.x > 0.01f,
                    "AirForce should not have been applied above max air speed");
        }

        [Test]
        public void Simulate_AirborneWithHorizontalVelocity_AppliesDragOppositeToHorizontal()
        {
            _state.IsGrounded = false;
            _state.DesiredVelocity = Vector3.zero;
            _body.Velocity = new Vector3(5f, 0f, 0f);

            _module.Simulate(Dt);

            bool foundDrag = false;
            foreach (var (force, mode) in _body.ForcesAdded)
            {
                if (mode == ForceMode.Acceleration && Mathf.Approximately(force.x, -5f * AirDrag))
                    foundDrag = true;
            }
            Assert.IsTrue(foundDrag, "Expected drag force of (-2.5, 0, 0)");
        }

        [Test]
        public void Simulate_AirStrafeEnabledBelowWishSpeed_AppliesStrafeImpulseAsVelocityChange()
        {
            SetPrivate("_enableAirStrafe", true);
            _state.IsGrounded = false;
            _state.DesiredVelocity = new Vector3(5f, 0f, 0f);
            _body.Velocity = Vector3.zero;

            _module.Simulate(Dt);

            bool foundStrafe = false;
            foreach (var (force, mode) in _body.ForcesAdded)
            {
                if (mode == ForceMode.VelocityChange && force.x > 0f)
                    foundStrafe = true;
            }
            Assert.IsTrue(foundStrafe, "Expected strafe impulse as VelocityChange in +X direction");
        }

        [Test]
        public void Simulate_AirStrafeEnabledAtWishSpeed_NoStrafeImpulse()
        {
            SetPrivate("_enableAirStrafe", true);
            _state.IsGrounded = false;
            _state.DesiredVelocity = new Vector3(5f, 0f, 0f);
            _body.Velocity = new Vector3(AirStrafeMaxWishSpeed, 0f, 0f);

            _module.Simulate(Dt);

            foreach (var (_, mode) in _body.ForcesAdded)
                Assert.AreNotEqual(
                    ForceMode.VelocityChange, mode,
                    "No VelocityChange forces expected at wish speed");
        }

        [Test]
        public void Simulate_PreserveTakeoffSpeed_RaisesEffectiveMaxAirSpeedToTakeoff()
        {
            SetPrivate("_preserveTakeoffSpeed", true);
            SetPrivate("_takeoffSpeed", 10f);
            _state.IsGrounded = false;
            _state.DesiredVelocity = new Vector3(1f, 0f, 0f);
            _body.Velocity = new Vector3(9f, 0f, 0f);

            _module.Simulate(Dt);

            bool foundAirForce = false;
            foreach (var (force, mode) in _body.ForcesAdded)
            {
                if (mode == ForceMode.Acceleration && force.x > 0.01f)
                    foundAirForce = true;
            }
            Assert.IsTrue(foundAirForce,
                "AirForce should apply when horizontal velocity is below takeoff-raised max");
        }

        [Test]
        public void Simulate_PreserveTakeoffSpeedBelowTakeoff_SkipsDrag()
        {
            SetPrivate("_preserveTakeoffSpeed", true);
            SetPrivate("_takeoffSpeed", 10f);
            _state.IsGrounded = false;
            _state.DesiredVelocity = Vector3.zero;
            _body.Velocity = new Vector3(9f, 0f, 0f);

            _module.Simulate(Dt);

            foreach (var (force, mode) in _body.ForcesAdded)
                Assert.IsFalse(
                    mode == ForceMode.Acceleration && force.x < -0.01f,
                    "Drag should have been skipped while below takeoff speed");
        }

        [Test]
        public void Simulate_AirborneFalling_AppliesGravityWithFallMultiplier()
        {
            _state.IsGrounded = false;
            _state.DesiredVelocity = Vector3.zero;
            _body.Velocity = new Vector3(0f, -5f, 0f);

            _module.Simulate(Dt);

            float expectedY = -Gravity * FallMultiplier;
            bool found = false;
            foreach (var (force, mode) in _body.ForcesAdded)
            {
                if (mode == ForceMode.Acceleration && Mathf.Approximately(force.y, expectedY))
                    found = true;
            }
            Assert.IsTrue(found, $"Expected gravity with fall multiplier: y={expectedY}");
        }

        [Test]
        public void Simulate_AirborneRising_AppliesPlainGravity()
        {
            _state.IsGrounded = false;
            _state.DesiredVelocity = Vector3.zero;
            _body.Velocity = new Vector3(0f, 5f, 0f);

            _module.Simulate(Dt);

            bool found = false;
            foreach (var (force, mode) in _body.ForcesAdded)
            {
                if (mode == ForceMode.Acceleration && Mathf.Approximately(force.y, -Gravity))
                    found = true;
            }
            Assert.IsTrue(found, $"Expected plain gravity: y={-Gravity}");
        }

        [Test]
        public void Simulate_ExternalForce_AppliedAsVelocityChangeEvenWhenSkipDefaultPhysics()
        {
            _state.ExternalForce = new Vector3(3f, 0f, 0f);
            _state.SkipDefaultPhysics = true;

            _module.Simulate(Dt);

            Assert.AreEqual(1, _body.ForcesAdded.Count);
            Assert.AreEqual(new Vector3(3f, 0f, 0f), _body.ForcesAdded[0].force);
            Assert.AreEqual(ForceMode.VelocityChange, _body.ForcesAdded[0].mode);
        }

        [Test]
        public void SaveRestore_Roundtrip_PreservesWasGroundedAndTakeoffSpeed()
        {
            SetPrivate("_wasGrounded", true);
            SetPrivate("_takeoffSpeed", 7.5f);

            var writer = new ModuleStateWriter(16);
            _module.SaveState(ref writer);
            var bytes = writer.ToArray();

            SetPrivate("_wasGrounded", false);
            SetPrivate("_takeoffSpeed", 0f);

            var reader = new ModuleStateReader(bytes);
            _module.RestoreState(ref reader);

            Assert.IsTrue(GetPrivate<bool>("_wasGrounded"));
            Assert.AreEqual(7.5f, GetPrivate<float>("_takeoffSpeed"), 0.0001f);
        }

        private void SetPrivate(string fieldName, object value)
        {
            var field = typeof(PhysicsResolverModule).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Private field {fieldName} not found on PhysicsResolverModule");
            field!.SetValue(_module, value);
        }

        private T GetPrivate<T>(string fieldName)
        {
            var field = typeof(PhysicsResolverModule).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Private field {fieldName} not found on PhysicsResolverModule");
            return (T)field!.GetValue(_module)!;
        }
    }
}
