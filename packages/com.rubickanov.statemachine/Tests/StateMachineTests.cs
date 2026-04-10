using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Rubickanov.StateMachine.Tests
{
    [TestFixture]
    public class StateMachineTests
    {
        private enum Key { A, B, C, D }

        private List<string> _log;
        private StateMachine<Key> _fsm;

        [SetUp]
        public void SetUp()
        {
            _log = new List<string>();
            _fsm = new StateMachine<Key>();
        }

        private RecordingState NewState(string name) => new RecordingState(name, _log);

        [Test]
        public void AddState_BeforeStart_IsRegistered()
        {
            _fsm.AddState(Key.A, NewState("A"));

            _fsm.Start(Key.A);

            Assert.AreEqual(Key.A, _fsm.CurrentKey);
        }

        [Test]
        public void AddState_AfterStart_Throws()
        {
            _fsm.AddState(Key.A, NewState("A"));
            _fsm.Start(Key.A);

            Assert.Throws<InvalidOperationException>(() => _fsm.AddState(Key.B, NewState("B")));
        }

        [Test]
        public void AddState_DuplicateKey_Throws()
        {
            _fsm.AddState(Key.A, NewState("A"));

            Assert.Throws<ArgumentException>(() => _fsm.AddState(Key.A, NewState("A2")));
        }

        [Test]
        public void Start_UnregisteredKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => _fsm.Start(Key.A));
        }

        [Test]
        public void Start_AlreadyStarted_Throws()
        {
            _fsm.AddState(Key.A, NewState("A"));
            _fsm.Start(Key.A);

            Assert.Throws<InvalidOperationException>(() => _fsm.Start(Key.A));
        }

        [Test]
        public void Start_InitialState_CallsOnEnter()
        {
            var a = NewState("A");
            _fsm.AddState(Key.A, a);

            _fsm.Start(Key.A);

            Assert.AreEqual(1, a.EnterCount);
            CollectionAssert.AreEqual(new[] { "A:Enter" }, _log);
        }

        [Test]
        public void Start_AfterStart_SetsIsStartedAndCurrentKey()
        {
            _fsm.AddState(Key.A, NewState("A"));

            _fsm.Start(Key.A);

            Assert.IsTrue(_fsm.IsStarted);
            Assert.AreEqual(Key.A, _fsm.CurrentKey);
            Assert.IsNotNull(_fsm.CurrentState);
        }

        [Test]
        public void Start_InitialEntry_DoesNotFireStateChanged()
        {
            var fired = false;
            _fsm.AddState(Key.A, NewState("A"));
            _fsm.StateChanged += (_, __) => fired = true;

            _fsm.Start(Key.A);

            Assert.IsFalse(fired);
        }

        [Test]
        public void CurrentKey_BeforeStart_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => { var _ = _fsm.CurrentKey; });
        }

        [Test]
        public void IsInState_BeforeStart_ReturnsFalse()
        {
            _fsm.AddState(Key.A, NewState("A"));

            Assert.IsFalse(_fsm.IsInState(Key.A));
        }

        [Test]
        public void IsInState_CurrentKey_ReturnsTrue()
        {
            _fsm.AddState(Key.A, NewState("A"));
            _fsm.AddState(Key.B, NewState("B"));
            _fsm.Start(Key.A);

            Assert.IsTrue(_fsm.IsInState(Key.A));
            Assert.IsFalse(_fsm.IsInState(Key.B));
        }

        [Test]
        public void Update_AfterStart_CallsOnUpdateWithDeltaTime()
        {
            var a = NewState("A");
            _fsm.AddState(Key.A, a);
            _fsm.Start(Key.A);

            _fsm.Update(0.25f);

            Assert.AreEqual(1, a.UpdateCount);
            Assert.AreEqual(0.25f, a.LastDelta);
        }

        [Test]
        public void Update_BeforeStart_IsNoOp()
        {
            Assert.DoesNotThrow(() => _fsm.Update(0.1f));
        }

        [Test]
        public void Stop_AfterStart_CallsOnExitAndClearsState()
        {
            var a = NewState("A");
            _fsm.AddState(Key.A, a);
            _fsm.Start(Key.A);

            _fsm.Stop();

            Assert.AreEqual(1, a.ExitCount);
            Assert.IsFalse(_fsm.IsStarted);
            Assert.IsNull(_fsm.CurrentState);
        }

        [Test]
        public void Stop_WhenNotStarted_IsNoOp()
        {
            Assert.DoesNotThrow(() => _fsm.Stop());
            Assert.IsFalse(_fsm.IsStarted);
        }

        [Test]
        public void Start_AfterStop_RestartsCleanly()
        {
            var a = NewState("A");
            _fsm.AddState(Key.A, a);
            _fsm.Start(Key.A);
            _fsm.Stop();

            _fsm.Start(Key.A);

            Assert.IsTrue(_fsm.IsStarted);
            Assert.AreEqual(2, a.EnterCount);
            Assert.AreEqual(1, a.ExitCount);
        }

        [Test]
        public void SetState_BeforeStart_Throws()
        {
            _fsm.AddState(Key.A, NewState("A"));

            Assert.Throws<InvalidOperationException>(() => _fsm.SetState(Key.A));
        }

        [Test]
        public void SetState_UnregisteredKey_Throws()
        {
            _fsm.AddState(Key.A, NewState("A"));
            _fsm.Start(Key.A);

            Assert.Throws<ArgumentException>(() => _fsm.SetState(Key.B));
        }

        [Test]
        public void SetState_Transition_CallsExitThenEnter()
        {
            _fsm.AddState(Key.A, NewState("A"));
            _fsm.AddState(Key.B, NewState("B"));
            _fsm.Start(Key.A);
            _log.Clear();

            _fsm.SetState(Key.B);

            CollectionAssert.AreEqual(new[] { "A:Exit", "B:Enter" }, _log);
            Assert.AreEqual(Key.B, _fsm.CurrentKey);
        }

        [Test]
        public void SetState_Transition_FiresStateChangedWithPrevAndNextKeys()
        {
            _fsm.AddState(Key.A, NewState("A"));
            _fsm.AddState(Key.B, NewState("B"));
            _fsm.Start(Key.A);

            Key prev = default, next = default;
            var fireCount = 0;
            _fsm.StateChanged += (p, n) =>
            {
                prev = p;
                next = n;
                fireCount++;
            };

            _fsm.SetState(Key.B);

            Assert.AreEqual(1, fireCount);
            Assert.AreEqual(Key.A, prev);
            Assert.AreEqual(Key.B, next);
        }

        [Test]
        public void SetState_CalledDuringOnEnter_IsDeferredUntilOnEnterReturns()
        {
            var a = NewState("A");
            var b = NewState("B");
            var c = NewState("C");
            b.OnEnterHook = () =>
            {
                Assert.AreEqual(Key.B, _fsm.CurrentKey);
                _fsm.SetState(Key.C);
                Assert.AreEqual(Key.B, _fsm.CurrentKey, "deferred — should still be B mid-OnEnter");
            };

            _fsm.AddState(Key.A, a);
            _fsm.AddState(Key.B, b);
            _fsm.AddState(Key.C, c);
            _fsm.Start(Key.A);
            _log.Clear();

            _fsm.SetState(Key.B);

            CollectionAssert.AreEqual(
                new[] { "A:Exit", "B:Enter", "B:Exit", "C:Enter" },
                _log);
            Assert.AreEqual(Key.C, _fsm.CurrentKey);
        }

        [Test]
        public void SetState_CalledDuringOnExit_IsDeferredUntilNextEnterCompletes()
        {
            var a = NewState("A");
            a.OnExitHook = () => _fsm.SetState(Key.C);
            var b = NewState("B");
            var c = NewState("C");

            _fsm.AddState(Key.A, a);
            _fsm.AddState(Key.B, b);
            _fsm.AddState(Key.C, c);
            _fsm.Start(Key.A);
            _log.Clear();

            _fsm.SetState(Key.B);

            CollectionAssert.AreEqual(
                new[] { "A:Exit", "B:Enter", "B:Exit", "C:Enter" },
                _log);
            Assert.AreEqual(Key.C, _fsm.CurrentKey);
        }

        [Test]
        public void Start_WithSetStateInInitialOnEnter_AppliesDeferredTransition()
        {
            var a = NewState("A");
            a.OnEnterHook = () => _fsm.SetState(Key.B);
            var b = NewState("B");

            _fsm.AddState(Key.A, a);
            _fsm.AddState(Key.B, b);

            _fsm.Start(Key.A);

            CollectionAssert.AreEqual(
                new[] { "A:Enter", "A:Exit", "B:Enter" },
                _log);
            Assert.AreEqual(Key.B, _fsm.CurrentKey);
        }

        [Test]
        public void SetState_PingPongingOnEnter_ThrowsAtMaxTransitionDepth()
        {
            var a = NewState("A");
            var b = NewState("B");
            a.OnEnterHook = () => _fsm.SetState(Key.B);
            b.OnEnterHook = () => _fsm.SetState(Key.A);

            _fsm.AddState(Key.A, a);
            _fsm.AddState(Key.B, b);

            var ex = Assert.Throws<InvalidOperationException>(() => _fsm.Start(Key.A));
            Assert.That(ex.Message, Does.Contain("transition depth"));
        }

        [Test]
        public void GetState_ExistingKeyAndMatchingType_ReturnsState()
        {
            var a = NewState("A");
            _fsm.AddState(Key.A, a);

            var got = _fsm.GetState<RecordingState>(Key.A);

            Assert.AreSame(a, got);
        }

        [Test]
        public void GetState_MismatchedType_ReturnsNull()
        {
            _fsm.AddState(Key.A, NewState("A"));

            var got = _fsm.GetState<OtherState>(Key.A);

            Assert.IsNull(got);
        }

        [Test]
        public void GetState_MissingKey_ReturnsNull()
        {
            Assert.IsNull(_fsm.GetState<RecordingState>(Key.A));
        }

        [Test]
        public void CustomComparer_IsUsedForStateLookup()
        {
            // Note: the custom comparer affects dictionary lookups (AddState/Start/SetState)
            // only — IsInState uses EqualityComparer<TKey>.Default (StateMachine.cs:59), so
            // this test pins the lookup behavior by checking CurrentState identity, not IsInState.
            var fsm = new StateMachine<string>(StringComparer.OrdinalIgnoreCase);
            var state = new RecordingState("S", new List<string>());
            fsm.AddState("State", state);

            fsm.Start("STATE");

            Assert.IsTrue(fsm.IsStarted);
            Assert.AreSame(state, fsm.CurrentState);
        }

        private class RecordingState : StateBase
        {
            public readonly string Name;
            public readonly List<string> Log;
            public int EnterCount;
            public int UpdateCount;
            public int ExitCount;
            public float LastDelta = -1f;
            public Action OnEnterHook;
            public Action OnExitHook;

            public RecordingState(string name, List<string> log)
            {
                Name = name;
                Log = log;
            }

            public override void OnEnter()
            {
                EnterCount++;
                Log.Add(Name + ":Enter");
                OnEnterHook?.Invoke();
            }

            public override void OnUpdate(float deltaTime)
            {
                UpdateCount++;
                LastDelta = deltaTime;
                Log.Add(Name + ":Update");
            }

            public override void OnExit()
            {
                ExitCount++;
                Log.Add(Name + ":Exit");
                OnExitHook?.Invoke();
            }
        }

        private class OtherState : StateBase { }
    }
}
