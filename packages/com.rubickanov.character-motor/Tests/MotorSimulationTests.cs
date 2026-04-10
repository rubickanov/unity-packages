using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    [TestFixture]
    public class MotorSimulationTests
    {
        private FakeMotorBody _body = default!;

        [SetUp]
        public void SetUp()
        {
            _body = new FakeMotorBody();
        }

        [TearDown]
        public void TearDown()
        {
            _body.Dispose();
        }

        [Test]
        public void Ctor_ModulesProvidedOutOfOrder_SortedByPriorityAndInitialized()
        {
            var log = new List<string>();
            var high = new RecordingModule(100, log, "high");
            var low = new RecordingModule(-50, log, "low");
            var mid = new RecordingModule(10, log, "mid");

            _ = new MotorSimulation(_body, new IMotorModule[] { high, low, mid });

            Assert.AreEqual(new[] { "init:low", "init:mid", "init:high" }, log.ToArray());
            Assert.AreEqual(1, high.InitializeCalls);
            Assert.AreEqual(1, low.InitializeCalls);
            Assert.AreEqual(1, mid.InitializeCalls);
        }

        [Test]
        public void Simulate_RunsModulesInPrioritySortedOrder()
        {
            var log = new List<string>();
            var high = new RecordingModule(100, log, "high");
            var low = new RecordingModule(-50, log, "low");
            var mid = new RecordingModule(10, log, "mid");
            var sim = new MotorSimulation(_body, new IMotorModule[] { high, low, mid });
            log.Clear();

            sim.Simulate(default, 0.02f);

            CollectionAssert.AreEqual(new[] { "sim:low", "sim:mid", "sim:high" }, log);
        }

        [Test]
        public void Simulate_InactiveModule_NotTicked()
        {
            var active = new RecordingModule(0, name: "active");
            var inactive = new RecordingModule(1, name: "inactive") { IsActive = false };
            var sim = new MotorSimulation(_body, new IMotorModule[] { active, inactive });

            sim.Simulate(default, 0.02f);

            Assert.AreEqual(1, active.SimulateCalls);
            Assert.AreEqual(0, inactive.SimulateCalls);
        }

        [Test]
        public void Simulate_AppliesInputToStateBeforeModulesTick()
        {
            var capture = new StateCapturingModule();
            var sim = new MotorSimulation(_body, new IMotorModule[] { capture });
            var input = new MotorInput { Move = new Vector2(1f, 0f), Jump = true };

            sim.Simulate(input, 0.02f);

            Assert.AreEqual(new Vector2(1f, 0f), capture.CapturedMoveInput);
            Assert.IsTrue(capture.CapturedJumpPressed);
        }

        [Test]
        public void Simulate_CallsBodyBeginFrameBeforeAndEndFrameAfterModules()
        {
            var log = new List<string>();
            _body.LifecycleLog = log;
            var module = new RecordingModule(0, log, "m");
            var sim = new MotorSimulation(_body, new IMotorModule[] { module });
            log.Clear();

            sim.Simulate(default, 0.02f);

            CollectionAssert.AreEqual(new[] { "begin", "sim:m", "end" }, log);
        }

        [Test]
        public void Simulate_FiresStateUpdatedOncePerTick()
        {
            var sim = new MotorSimulation(_body, new IMotorModule[] { new RecordingModule(0) });
            int fired = 0;
            sim.StateUpdated += _ => fired++;

            sim.Simulate(default, 0.02f);
            sim.Simulate(default, 0.02f);

            Assert.AreEqual(2, fired);
        }

        [Test]
        public void Simulate_StateUpdated_ReceivesSnapshotOfCurrentState()
        {
            var writer = new StateWriterModule(isGrounded: true, speed: 5f);
            var sim = new MotorSimulation(_body, new IMotorModule[] { writer });
            MotorSnapshot? captured = null;
            sim.StateUpdated += s => captured = s;

            sim.Simulate(default, 0.02f);

            Assert.IsTrue(captured.HasValue);
            Assert.IsTrue(captured!.Value.IsGrounded);
            Assert.AreEqual(5f, captured.Value.HorizontalSpeed, 0.0001f);
        }

        [Test]
        public void Simulate_AfterTick_PerFrameStateReset()
        {
            var writer = new StateWriterModule(speedMultiplier: 3f, externalForce: new Vector3(1f, 0f, 0f));
            var sim = new MotorSimulation(_body, new IMotorModule[] { writer });

            sim.Simulate(default, 0.02f);

            // SpeedMultiplier and ExternalForce reset between ticks
            Assert.AreEqual(1f, sim.State.SpeedMultiplier);
            Assert.AreEqual(Vector3.zero, sim.State.GroundVelocity);
        }

        [Test]
        public void Simulate_ExternalSpeedMultiplier_StacksMultiplicativelyOntoSpeedMultiplier()
        {
            var capture = new StateCapturingModule(priority: 1000);
            var writer = new StateWriterModule(speedMultiplier: 2f);
            var sim = new MotorSimulation(_body, new IMotorModule[] { writer, capture });
            sim.SetExternalSpeedMultiplier(0.5f);

            sim.Simulate(default, 0.02f);

            // Start=1 * externalMultiplier(0.5) = 0.5, then writer multiplies by 2 → 1.0
            Assert.AreEqual(1f, capture.CapturedSpeedMultiplier, 0.0001f);
        }

        [Test]
        public void AddPendingForce_AppliesToExternalForceOnNextSimulateAndClears()
        {
            var capture = new StateCapturingModule();
            var sim = new MotorSimulation(_body, new IMotorModule[] { capture });
            sim.AddPendingForce(new Vector3(5f, 0f, 0f));
            sim.AddPendingForce(new Vector3(0f, 0f, 3f));

            sim.Simulate(default, 0.02f);
            var firstTick = capture.CapturedExternalForce;

            sim.Simulate(default, 0.02f);
            var secondTick = capture.CapturedExternalForce;

            Assert.AreEqual(new Vector3(5f, 0f, 3f), firstTick);
            Assert.AreEqual(Vector3.zero, secondTick);
        }

        [Test]
        public void AddModule_AtRuntime_InitializedAndInsertedInSortOrder()
        {
            var log = new List<string>();
            var first = new RecordingModule(0, log, "first");
            var sim = new MotorSimulation(_body, new IMotorModule[] { first });
            log.Clear();

            var early = new RecordingModule(-100, log, "early");
            sim.AddModule(early);
            sim.Simulate(default, 0.02f);

            CollectionAssert.AreEqual(new[] { "init:early", "sim:early", "sim:first" }, log);
        }

        [Test]
        public void RemoveModule_Present_ReturnsTrueAndStopsTicking()
        {
            var module = new RecordingModule(0);
            var sim = new MotorSimulation(_body, new IMotorModule[] { module });

            bool removed = sim.RemoveModule(module);
            sim.Simulate(default, 0.02f);

            Assert.IsTrue(removed);
            Assert.AreEqual(0, module.SimulateCalls);
        }

        [Test]
        public void RemoveModule_NotPresent_ReturnsFalse()
        {
            var sim = new MotorSimulation(_body, new IMotorModule[] { new RecordingModule(0) });

            bool removed = sim.RemoveModule(new RecordingModule(0));

            Assert.IsFalse(removed);
        }

        [Test]
        public void GetModule_ByTypePresent_ReturnsInstance()
        {
            var module = new RecordingModule(0);
            var sim = new MotorSimulation(_body, new IMotorModule[] { module });

            var fetched = sim.GetModule<RecordingModule>();

            Assert.AreSame(module, fetched);
        }

        [Test]
        public void GetModule_ByTypeMissing_ReturnsNull()
        {
            var sim = new MotorSimulation(_body, new IMotorModule[] { new RecordingModule(0) });

            var fetched = sim.GetModule<FakeStatefulModule>();

            Assert.IsNull(fetched);
        }

        [Test]
        public void VisualUpdate_RunsVisualUpdateOnActiveModulesOnly()
        {
            var active = new RecordingModule(0, name: "active");
            var inactive = new RecordingModule(1, name: "inactive") { IsActive = false };
            var sim = new MotorSimulation(_body, new IMotorModule[] { active, inactive });

            sim.VisualUpdate(0.016f);

            Assert.AreEqual(1, active.VisualUpdateCalls);
            Assert.AreEqual(0, inactive.VisualUpdateCalls);
        }

        [Test]
        public void SaveState_CapturesBodyAndScalarMotorStateAndStatefulModuleBytes()
        {
            _body.Position = new Vector3(1f, 2f, 3f);
            _body.Velocity = new Vector3(4f, 5f, 6f);
            _body.CapsuleHeight = 1.8f;
            var writer = new StateWriterModule(isGrounded: true, speed: 7f, speedMultiplier: 2f);
            var stateful = new FakeStatefulModule(priority: 500);
            var sim = new MotorSimulation(_body, new IMotorModule[] { writer, stateful });
            sim.Simulate(default, 0.02f);

            var snapshot = sim.SaveState();

            Assert.AreEqual(new Vector3(1f, 2f, 3f), snapshot.Body.Position);
            Assert.AreEqual(new Vector3(4f, 5f, 6f), snapshot.Body.Velocity);
            Assert.AreEqual(1.8f, snapshot.Body.CapsuleHeight);
            Assert.IsNotNull(snapshot.ModuleStates);
            Assert.Greater(snapshot.ModuleStates!.Length, 0);
        }

        [Test]
        public void RestoreState_AfterMutation_ReturnsStatefulModuleAndBodyToCapturedValues()
        {
            var stateful = new FakeStatefulModule(priority: 500);
            var sim = new MotorSimulation(_body, new IMotorModule[] { stateful });
            _body.Position = new Vector3(10f, 20f, 30f);
            sim.Simulate(default, 0.02f);
            var snapshot = sim.SaveState();

            stateful.Counter = 999;
            _body.Position = Vector3.zero;
            sim.RestoreState(snapshot);

            Assert.AreEqual(1, stateful.Counter);
            Assert.AreEqual(new Vector3(10f, 20f, 30f), _body.Position);
        }

        [Test]
        public void RestoreState_RestoresAllScalarStateFields()
        {
            var sim = new MotorSimulation(_body, new IMotorModule[] { });
            _body.Velocity = new Vector3(1f, 2f, 3f);
            // Simulate once to get a baseline, then mutate the internal state via a writer
            var writer = new StateWriterModule(
                isGrounded: true,
                speed: 10f,
                speedMultiplier: 3f,
                groundAngle: 30f,
                isSprinting: true,
                isCrouching: true);
            var sim2 = new MotorSimulation(_body, new IMotorModule[] { writer });
            sim2.Simulate(default, 0.02f);
            // State after Simulate has been ResetPerFrame'd — SpeedMultiplier=1, IsSliding=false
            // But persistent fields remain. Capture now.
            var snapshot = sim2.SaveState();

            // Mutate body and restore
            _body.Velocity = Vector3.zero;
            sim2.RestoreState(snapshot);

            Assert.AreEqual(10f, sim2.State.CurrentVelocity.x); // writer set CurrentVelocity.x = speed
            Assert.IsTrue(sim2.State.IsGrounded);
            Assert.IsTrue(sim2.State.IsSprinting);
            Assert.IsTrue(sim2.State.IsCrouching);
            Assert.AreEqual(30f, sim2.State.GroundAngle);
        }

        // ---------- Test modules ----------

        /// <summary>
        /// Captures the state snapshot observed at the moment this module ticks.
        /// Used to assert the simulation applied input and stacked multipliers
        /// before modules ran.
        /// </summary>
        private sealed class StateCapturingModule : IMotorModule
        {
            public int Priority { get; }
            public bool IsActive { get; set; } = true;

            private MotorState _state = default!;

            public Vector2 CapturedMoveInput;
            public bool CapturedJumpPressed;
            public float CapturedSpeedMultiplier;
            public Vector3 CapturedExternalForce;

            public StateCapturingModule(int priority = 0) { Priority = priority; }

            public void Initialize(MotorState state, IMotorBody body, IModuleResolver resolver)
            {
                _state = state;
            }

            public void Simulate(float deltaTime)
            {
                CapturedMoveInput = _state.MoveInput;
                CapturedJumpPressed = _state.JumpPressed;
                CapturedSpeedMultiplier = _state.SpeedMultiplier;
                CapturedExternalForce = _state.ExternalForce;
            }

            public void VisualUpdate(float deltaTime) { }
        }

        /// <summary>
        /// Writes a fixed set of values into <see cref="MotorState"/> every tick.
        /// Used to seed state fields that <see cref="MotorSimulation.StateUpdated"/>
        /// and <see cref="MotorSimulation.SaveState"/> should observe.
        /// </summary>
        private sealed class StateWriterModule : IMotorModule
        {
            public int Priority => 500;
            public bool IsActive { get; set; } = true;

            private readonly bool _setIsGrounded;
            private readonly float _speed;
            private readonly float _speedMultiplier;
            private readonly Vector3 _externalForce;
            private readonly float _groundAngle;
            private readonly bool _setSprinting;
            private readonly bool _setCrouching;

            private MotorState _state = default!;

            public StateWriterModule(
                bool isGrounded = false,
                float speed = 0f,
                float speedMultiplier = 1f,
                Vector3 externalForce = default,
                float groundAngle = 0f,
                bool isSprinting = false,
                bool isCrouching = false)
            {
                _setIsGrounded = isGrounded;
                _speed = speed;
                _speedMultiplier = speedMultiplier;
                _externalForce = externalForce;
                _groundAngle = groundAngle;
                _setSprinting = isSprinting;
                _setCrouching = isCrouching;
            }

            public void Initialize(MotorState state, IMotorBody body, IModuleResolver resolver)
            {
                _state = state;
            }

            public void Simulate(float deltaTime)
            {
                _state.IsGrounded = _setIsGrounded;
                _state.CurrentVelocity = new Vector3(_speed, 0f, 0f);
                _state.SpeedMultiplier *= _speedMultiplier;
                _state.ExternalForce += _externalForce;
                _state.GroundAngle = _groundAngle;
                _state.IsSprinting = _setSprinting;
                _state.IsCrouching = _setCrouching;
            }

            public void VisualUpdate(float deltaTime) { }
        }

        /// <summary>
        /// Minimal <see cref="IStatefulModule"/> used to verify
        /// <see cref="MotorSimulation.SaveState"/> / <see cref="MotorSimulation.RestoreState"/>
        /// serialize per-module state through <see cref="ModuleStateWriter"/>.
        /// </summary>
        private sealed class FakeStatefulModule : IMotorModule, IStatefulModule
        {
            public int Priority { get; }
            public bool IsActive { get; set; } = true;

            public int Counter;

            public FakeStatefulModule(int priority) { Priority = priority; }

            public void Initialize(MotorState state, IMotorBody body, IModuleResolver resolver) { }
            public void Simulate(float deltaTime) { Counter++; }
            public void VisualUpdate(float deltaTime) { }

            public void SaveState(ref ModuleStateWriter writer) => writer.Write(Counter);
            public void RestoreState(ref ModuleStateReader reader) => Counter = reader.ReadInt();
        }
    }
}
