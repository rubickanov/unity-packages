using System.Reflection;
using NUnit.Framework;
using Rubickanov.Motor.Modules;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class StepClimbModuleTests
    {
        private const float Dt = 0.02f;
        private const float MaxStepHeight = 0.35f;
        private const float StepClimbSpeed = 5f;

        private MotorState _state = default!;
        private FakeMotorBody _body = default!;
        private StepClimbModule _module = default!;

        [SetUp]
        public void SetUp()
        {
            _state = new MotorState();
            _body = new FakeMotorBody();
            _module = new StepClimbModule();
            _module.Initialize(_state, _body, new NullModuleResolver());
        }

        [TearDown]
        public void TearDown()
        {
            _body.Dispose();
        }

        [Test]
        public void Simulate_NoForwardObstacle_DoesNotScheduleClimb()
        {
            ArrangeMovingForward();
            _body.RaycastHandler = (origin, dir, dist) => null;

            _module.Simulate(Dt);

            Assert.AreEqual(0f, GetPrivate<float>("_pendingStepUp"));
        }

        [Test]
        public void Simulate_WallBlocksStepHeight_DoesNotScheduleClimb()
        {
            ArrangeMovingForward();
            // Both low and high raycasts hit — it's a wall, not a step
            _body.RaycastHandler = (origin, dir, dist) =>
                RaycastHitBuilder.Build(origin + dir * 0.2f, -dir, 0.2f);

            _module.Simulate(Dt);

            Assert.AreEqual(0f, GetPrivate<float>("_pendingStepUp"));
        }

        [Test]
        public void Simulate_StepSurfaceTooSteep_DoesNotScheduleClimb()
        {
            ArrangeMovingForward();
            _body.RaycastHandler = (origin, dir, dist) =>
                origin.y < 0.1f
                    ? RaycastHitBuilder.Build(origin + dir * 0.2f, -dir, 0.2f)
                    : (RaycastHit?)null;
            // Step surface tilted 60° from up — exceeds 46° slope limit
            _body.SphereCastHandler = (origin, radius, dir, dist) =>
                RaycastHitBuilder.Build(
                    new Vector3(origin.x, 0.2f, origin.z),
                    Quaternion.Euler(60f, 0f, 0f) * Vector3.up,
                    0.05f);

            _module.Simulate(Dt);

            Assert.AreEqual(0f, GetPrivate<float>("_pendingStepUp"));
        }

        [Test]
        public void Simulate_Airborne_DoesNotScheduleClimb()
        {
            ArrangeMovingForward();
            _state.IsGrounded = false;
            ArrangeValidStepScene(stepSurfaceY: 0.2f);

            _module.Simulate(Dt);

            Assert.AreEqual(0f, GetPrivate<float>("_pendingStepUp"));
        }

        [Test]
        public void Simulate_NoMoveInput_DoesNotScheduleClimb()
        {
            _state.IsGrounded = true;
            _state.MoveInput = Vector2.zero;
            _state.DesiredVelocity = Vector3.zero;
            ArrangeValidStepScene(stepSurfaceY: 0.2f);

            _module.Simulate(Dt);

            Assert.AreEqual(0f, GetPrivate<float>("_pendingStepUp"));
        }

        [Test]
        public void Simulate_ValidStep_SchedulesPendingStepUp()
        {
            ArrangeMovingForward();
            ArrangeValidStepScene(stepSurfaceY: 0.2f);

            _module.Simulate(Dt);

            // Module adds a tiny 0.01 epsilon on top of the measured step height
            Assert.AreEqual(0.21f, GetPrivate<float>("_pendingStepUp"), 0.0001f);
        }

        [Test]
        public void Simulate_PendingClimbLargerThanStepPerTick_LiftsByClimbSpeedTimesDt()
        {
            SetPrivate("_pendingStepUp", 0.3f);

            _module.Simulate(Dt);

            Assert.AreEqual(1, _body.MoveCalls.Count);
            Assert.AreEqual(StepClimbSpeed * Dt, _body.Position.y, 0.0001f);
        }

        [Test]
        public void Simulate_PendingClimbSmallerThanStepPerTick_LiftsByRemainingPending()
        {
            SetPrivate("_pendingStepUp", 0.05f);

            _module.Simulate(Dt);

            Assert.AreEqual(0.05f, _body.Position.y, 0.0001f);
            Assert.AreEqual(0f, GetPrivate<float>("_pendingStepUp"), 0.0001f);
        }

        [Test]
        public void Simulate_CapsuleHeightShrunk_ScalesMaxStepHeightProportionally()
        {
            _body.CapsuleHeight = 1f; // Half of initial 2 → maxStep halves to 0.175
            ArrangeMovingForward();
            // Step at 0.2 — below default maxStep (0.35) but above shrunk maxStep (0.175)
            ArrangeValidStepScene(stepSurfaceY: 0.2f);

            _module.Simulate(Dt);

            Assert.AreEqual(0f, GetPrivate<float>("_pendingStepUp"));
        }

        [Test]
        public void SaveRestore_Roundtrip_PreservesPendingStepUpAndInitialCapsuleHeight()
        {
            SetPrivate("_pendingStepUp", 0.25f);
            SetPrivate("_initialCapsuleHeight", 1.8f);

            var writer = new ModuleStateWriter(16);
            _module.SaveState(ref writer);
            var bytes = writer.ToArray();

            SetPrivate("_pendingStepUp", 0f);
            SetPrivate("_initialCapsuleHeight", 5f);

            var reader = new ModuleStateReader(bytes);
            _module.RestoreState(ref reader);

            Assert.AreEqual(0.25f, GetPrivate<float>("_pendingStepUp"), 0.0001f);
            Assert.AreEqual(1.8f, GetPrivate<float>("_initialCapsuleHeight"), 0.0001f);
        }

        private void ArrangeMovingForward()
        {
            _state.IsGrounded = true;
            _state.MoveInput = new Vector2(0f, 1f);
            _state.DesiredVelocity = new Vector3(0f, 0f, 1f);
        }

        private void ArrangeValidStepScene(float stepSurfaceY)
        {
            _body.RaycastHandler = (origin, dir, dist) =>
                origin.y < 0.1f
                    ? RaycastHitBuilder.Build(origin + dir * 0.2f, -dir, 0.2f)
                    : (RaycastHit?)null;
            _body.SphereCastHandler = (origin, radius, dir, dist) =>
                RaycastHitBuilder.Build(
                    new Vector3(origin.x, stepSurfaceY, origin.z),
                    Vector3.up,
                    0.05f);
        }

        private void SetPrivate(string fieldName, object value)
        {
            var field = typeof(StepClimbModule).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Private field {fieldName} not found on StepClimbModule");
            field!.SetValue(_module, value);
        }

        private T GetPrivate<T>(string fieldName)
        {
            var field = typeof(StepClimbModule).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Private field {fieldName} not found on StepClimbModule");
            return (T)field!.GetValue(_module)!;
        }
    }
}
