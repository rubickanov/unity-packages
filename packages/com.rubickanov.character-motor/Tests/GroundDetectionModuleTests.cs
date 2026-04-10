using NUnit.Framework;
using Rubickanov.Motor.Modules;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class GroundDetectionModuleTests
    {
        private const float MaxSlope = 46f;

        private MotorState _state = default!;
        private FakeMotorBody _body = default!;
        private GroundDetectionModule _module = default!;
        private GameObject _groundGo = default!;
        private BoxCollider _groundCollider = default!;

        [SetUp]
        public void SetUp()
        {
            _state = new MotorState();
            _body = new FakeMotorBody();
            _module = new GroundDetectionModule();
            _module.Initialize(_state, _body, new NullModuleResolver());

            _groundGo = new GameObject("Ground");
            _groundCollider = _groundGo.AddComponent<BoxCollider>();
        }

        [TearDown]
        public void TearDown()
        {
            _body.Dispose();
            Object.DestroyImmediate(_groundGo);
        }

        [Test]
        public void Simulate_NoSphereCastHit_UngroundedWithDefaultNormal()
        {
            _body.NextSphereCastHit = null;

            _module.Simulate(0.02f);

            Assert.IsFalse(_state.IsGrounded);
            Assert.AreEqual(Vector3.up, _state.GroundNormal);
            Assert.AreEqual(0f, _state.GroundAngle);
            Assert.AreEqual(Vector3.zero, _state.GroundVelocity);
            Assert.IsTrue(_state.IsInAir);
        }

        [Test]
        public void Simulate_HitFlatGround_GroundedWithZeroAngleAndUpNormal()
        {
            _body.NextSphereCastHit = RaycastHitBuilder.Build(
                point: Vector3.zero,
                normal: Vector3.up,
                distance: 0.05f,
                collider: _groundCollider);

            _module.Simulate(0.02f);

            Assert.IsTrue(_state.IsGrounded);
            Assert.AreEqual(Vector3.up, _state.GroundNormal);
            Assert.AreEqual(0f, _state.GroundAngle, 0.0001f);
            Assert.IsFalse(_state.IsInAir);
        }

        [Test]
        public void Simulate_HitWalkableSlope_GroundedWithMatchingAngleAndNormal()
        {
            // 30° slope — normal rotated 30° from up around X axis
            Vector3 normal = Quaternion.Euler(30f, 0f, 0f) * Vector3.up;
            _body.NextSphereCastHit = RaycastHitBuilder.Build(Vector3.zero, normal, 0.05f, _groundCollider);

            _module.Simulate(0.02f);

            Assert.IsTrue(_state.IsGrounded);
            Assert.AreEqual(30f, _state.GroundAngle, 0.01f);
            Assert.AreEqual(normal, _state.GroundNormal);
        }

        [Test]
        public void Simulate_HitSteeperThanMaxSlope_NotGroundedButNormalAndAngleRecorded()
        {
            Vector3 normal = Quaternion.Euler(60f, 0f, 0f) * Vector3.up;
            _body.NextSphereCastHit = RaycastHitBuilder.Build(Vector3.zero, normal, 0.05f, _groundCollider);

            _module.Simulate(0.02f);

            Assert.IsFalse(_state.IsGrounded);
            Assert.AreEqual(60f, _state.GroundAngle, 0.01f);
            Assert.AreEqual(normal, _state.GroundNormal);
            Assert.IsTrue(_state.IsInAir);
        }

        [Test]
        public void Simulate_GroundedToUngroundedTransition_FiresGroundedChangedFalseOnce()
        {
            _body.NextSphereCastHit = RaycastHitBuilder.Build(Vector3.zero, Vector3.up, 0.05f, _groundCollider);
            _module.Simulate(0.02f);

            bool? received = null;
            int calls = 0;
            _module.GroundedChanged += g => { received = g; calls++; };
            _body.NextSphereCastHit = null;

            _module.Simulate(0.02f);

            Assert.AreEqual(1, calls);
            Assert.IsFalse(received);
        }

        [Test]
        public void Simulate_UngroundedToGroundedTransition_FiresGroundedChangedTrueOnce()
        {
            _body.NextSphereCastHit = null;
            _module.Simulate(0.02f);

            bool? received = null;
            int calls = 0;
            _module.GroundedChanged += g => { received = g; calls++; };
            _body.NextSphereCastHit = RaycastHitBuilder.Build(Vector3.zero, Vector3.up, 0.05f, _groundCollider);

            _module.Simulate(0.02f);

            Assert.AreEqual(1, calls);
            Assert.IsTrue(received);
        }

        [Test]
        public void Simulate_GroundedStateUnchanged_DoesNotFireGroundedChanged()
        {
            _body.NextSphereCastHit = RaycastHitBuilder.Build(Vector3.zero, Vector3.up, 0.05f, _groundCollider);
            _module.Simulate(0.02f);

            int calls = 0;
            _module.GroundedChanged += _ => calls++;

            _module.Simulate(0.02f);
            _module.Simulate(0.02f);

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Simulate_HitColliderWithAttachedRigidbody_GroundVelocityCopiedFromRigidbodyPointVelocity()
        {
            var platformGo = new GameObject("Platform");
            try
            {
                var platformCollider = platformGo.AddComponent<BoxCollider>();
                var platformRb = platformGo.AddComponent<Rigidbody>();
                platformRb.useGravity = false;
                platformRb.linearVelocity = new Vector3(2f, 0f, 0f);

                _body.NextSphereCastHit = RaycastHitBuilder.Build(
                    point: Vector3.zero,
                    normal: Vector3.up,
                    distance: 0.05f,
                    collider: platformCollider);

                _module.Simulate(0.02f);

                Assert.IsTrue(_state.IsGrounded);
                Assert.AreEqual(new Vector3(2f, 0f, 0f), _state.GroundVelocity);
            }
            finally
            {
                Object.DestroyImmediate(platformGo);
            }
        }

        [Test]
        public void Simulate_HitColliderWithoutRigidbody_GroundVelocityStaysZero()
        {
            _body.NextSphereCastHit = RaycastHitBuilder.Build(Vector3.zero, Vector3.up, 0.05f, _groundCollider);

            _module.Simulate(0.02f);

            Assert.AreEqual(Vector3.zero, _state.GroundVelocity);
        }
    }
}
