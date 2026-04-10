using NUnit.Framework;
using Rubickanov.Motor.Modules;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class CrouchModuleTests
    {
        private const float StandHeight = 2f;
        private const float CrouchHeight = 1.2f;
        private const float CrouchSpeedMultiplier = 0.45f;
        private const float TransitionSpeed = 10f;
        private const float Dt = 0.02f;

        private MotorState _state = default!;
        private FakeMotorBody _body = default!;
        private CrouchModule _module = default!;

        [SetUp]
        public void SetUp()
        {
            _state = new MotorState();
            _body = new FakeMotorBody();
            _module = new CrouchModule();
            _module.Initialize(_state, _body, new NullModuleResolver());
        }

        [TearDown]
        public void TearDown()
        {
            _body.Dispose();
        }

        [Test]
        public void Simulate_CrouchPressedFromStanding_SetsIsCrouchingAndFiresCrouchChangedTrue()
        {
            bool? received = null;
            int calls = 0;
            _module.CrouchChanged += c => { received = c; calls++; };

            _state.CrouchPressed = true;
            _module.Simulate(Dt);

            Assert.IsTrue(_state.IsCrouching);
            Assert.AreEqual(1, calls);
            Assert.IsTrue(received);
        }

        [Test]
        public void Simulate_CrouchActive_MultipliesSpeedByCrouchSpeedMultiplier()
        {
            _state.CrouchPressed = true;

            _module.Simulate(Dt);

            Assert.IsTrue(_state.IsCrouching);
            Assert.AreEqual(CrouchSpeedMultiplier, _state.SpeedMultiplier, 0.0001f);
        }

        [Test]
        public void Simulate_CrouchPressed_HeightMovesTowardCrouchHeightByTransitionSpeed()
        {
            _state.CrouchPressed = true;

            _module.Simulate(Dt);

            float expected = Mathf.MoveTowards(StandHeight, CrouchHeight, TransitionSpeed * Dt);
            Assert.AreEqual(expected, _body.CapsuleHeight, 0.0001f);
        }

        [Test]
        public void Simulate_CrouchPressedUnderCeiling_StaysCrouched()
        {
            _state.CrouchPressed = true;
            _module.Simulate(Dt);
            Assert.IsTrue(_state.IsCrouching, "Precondition: entered crouch");

            var ceilingGo = new GameObject("Ceiling");
            try
            {
                var ceilingCollider = ceilingGo.AddComponent<BoxCollider>();
                _body.SphereCastNonAllocResults = new[]
                {
                    RaycastHitBuilder.Build(Vector3.up, Vector3.down, 0.1f, ceilingCollider)
                };
                _body.SphereCastNonAllocCount = 1;

                _state.CrouchPressed = true;
                _module.Simulate(Dt);

                Assert.IsTrue(_state.IsCrouching);
            }
            finally
            {
                Object.DestroyImmediate(ceilingGo);
            }
        }

        [Test]
        public void Simulate_CrouchPressedClearCeiling_ReturnsToStanding()
        {
            _state.CrouchPressed = true;
            _module.Simulate(Dt);
            Assert.IsTrue(_state.IsCrouching, "Precondition: entered crouch");

            _state.CrouchPressed = true;
            _module.Simulate(Dt);

            Assert.IsFalse(_state.IsCrouching);
        }

        [Test]
        public void Simulate_CeilingCheck_IgnoresHitsAttachedToOwnRigidbody()
        {
            _state.CrouchPressed = true;
            _module.Simulate(Dt);
            Assert.IsTrue(_state.IsCrouching, "Precondition: entered crouch");

            var childGo = new GameObject("OwnCollider");
            try
            {
                childGo.transform.SetParent(_body.Transform);
                var childCollider = childGo.AddComponent<BoxCollider>();
                _body.SphereCastNonAllocResults = new[]
                {
                    RaycastHitBuilder.Build(Vector3.up, Vector3.down, 0.1f, childCollider)
                };
                _body.SphereCastNonAllocCount = 1;

                _state.CrouchPressed = true;
                _module.Simulate(Dt);

                Assert.IsFalse(_state.IsCrouching);
            }
            finally
            {
                Object.DestroyImmediate(childGo);
            }
        }

        [Test]
        public void SaveRestore_Roundtrip_RestoresCrouchStateAndHeight()
        {
            _state.CrouchPressed = true;
            _module.Simulate(Dt);
            _state.CrouchPressed = false;
            _module.Simulate(Dt);
            float savedHeight = _body.CapsuleHeight;

            var writer = new ModuleStateWriter(16);
            _module.SaveState(ref writer);
            var bytes = writer.ToArray();

            _state.CrouchPressed = true;
            _module.Simulate(Dt);
            Assert.IsFalse(_state.IsCrouching, "Precondition: uncrouched before restore");

            var reader = new ModuleStateReader(bytes);
            _module.RestoreState(ref reader);
            _state.CrouchPressed = false;
            _module.Simulate(Dt);

            Assert.IsTrue(_state.IsCrouching);
            float expected = Mathf.MoveTowards(savedHeight, CrouchHeight, TransitionSpeed * Dt);
            Assert.AreEqual(expected, _body.CapsuleHeight, 0.0001f);
        }
    }
}
