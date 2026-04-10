using NUnit.Framework;
using Rubickanov.Motor.Modules;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class SlopeSlideModuleTests
    {
        private const float Dt = 0.02f;
        private const float SlideAngle = 46f;
        private const float HardSlideAngle = 70f;
        private const float SlideAcceleration = 15f;

        private MotorState _state = default!;
        private FakeMotorBody _body = default!;
        private SlopeSlideModule _module = default!;

        [SetUp]
        public void SetUp()
        {
            _state = new MotorState();
            _body = new FakeMotorBody();
            _module = new SlopeSlideModule();
            _module.Initialize(_state, _body, new NullModuleResolver());
        }

        [TearDown]
        public void TearDown()
        {
            _body.Dispose();
        }

        [Test]
        public void Simulate_Airborne_NoForceApplied()
        {
            _state.IsGrounded = false;
            _state.GroundAngle = 80f;

            _module.Simulate(Dt);

            Assert.AreEqual(0, _body.ForcesAdded.Count);
            Assert.IsFalse(_state.IsSliding);
        }

        [Test]
        public void Simulate_GroundedBelowSlideAngle_NoForceApplied()
        {
            _state.IsGrounded = true;
            _state.GroundAngle = SlideAngle - 10f;
            _state.GroundNormal = Quaternion.Euler(SlideAngle - 10f, 0f, 0f) * Vector3.up;

            _module.Simulate(Dt);

            Assert.AreEqual(0, _body.ForcesAdded.Count);
            Assert.IsFalse(_state.IsSliding);
        }

        [Test]
        public void Simulate_GroundedAtSlideAngle_NoForceApplied()
        {
            _state.IsGrounded = true;
            _state.GroundAngle = SlideAngle;
            _state.GroundNormal = Quaternion.Euler(SlideAngle, 0f, 0f) * Vector3.up;

            _module.Simulate(Dt);

            Assert.AreEqual(0, _body.ForcesAdded.Count);
        }

        [Test]
        public void Simulate_GroundedAboveSlideAngle_AppliesDownslopeForce()
        {
            _state.IsGrounded = true;
            _state.GroundAngle = SlideAngle + 4f;
            _state.GroundNormal = Quaternion.Euler(SlideAngle + 4f, 0f, 0f) * Vector3.up;

            _module.Simulate(Dt);

            Assert.AreEqual(1, _body.ForcesAdded.Count);
            Assert.AreEqual(ForceMode.Acceleration, _body.ForcesAdded[0].mode);
            // Downslope direction: -Y and +Z for a slope tilted forward
            Assert.Less(_body.ForcesAdded[0].force.y, 0f);
            Assert.Greater(_body.ForcesAdded[0].force.z, 0f);
        }

        [Test]
        public void Simulate_GroundedAboveSlideAngle_SetsIsSliding()
        {
            _state.IsGrounded = true;
            _state.GroundAngle = SlideAngle + 4f;
            _state.GroundNormal = Quaternion.Euler(SlideAngle + 4f, 0f, 0f) * Vector3.up;

            _module.Simulate(Dt);

            Assert.IsTrue(_state.IsSliding);
        }

        [Test]
        public void Simulate_GroundedAtOrAboveHardSlideAngle_SetsSkipDefaultPhysics()
        {
            _state.IsGrounded = true;
            _state.GroundAngle = HardSlideAngle + 5f;
            _state.GroundNormal = Quaternion.Euler(HardSlideAngle + 5f, 0f, 0f) * Vector3.up;

            _module.Simulate(Dt);

            Assert.IsTrue(_state.SkipDefaultPhysics);
        }

        [Test]
        public void Simulate_GroundedBelowHardSlideAngle_DoesNotSetSkipDefaultPhysics()
        {
            _state.IsGrounded = true;
            _state.GroundAngle = SlideAngle + 4f;
            _state.GroundNormal = Quaternion.Euler(SlideAngle + 4f, 0f, 0f) * Vector3.up;

            _module.Simulate(Dt);

            Assert.IsFalse(_state.SkipDefaultPhysics);
        }

        [Test]
        public void Simulate_ForceScale_ProportionalToAngleBetweenSlideAndNinety()
        {
            // Halfway between 46 and 90 → scale = 0.5, expected force magnitude = 7.5
            float midAngle = (SlideAngle + 90f) * 0.5f;
            _state.IsGrounded = true;
            _state.GroundAngle = midAngle;
            _state.GroundNormal = Quaternion.Euler(midAngle, 0f, 0f) * Vector3.up;

            _module.Simulate(Dt);

            Assert.AreEqual(SlideAcceleration * 0.5f, _body.ForcesAdded[0].force.magnitude, 0.0001f);
        }
    }
}
