using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Rubickanov.StateMachine.Tests
{
    [TestFixture]
    public class AsyncStateMachineTests
    {
        private enum Key { A, B, C, D }

        private List<string> _log;
        private AsyncStateMachine<Key> _fsm;

        [SetUp]
        public void SetUp()
        {
            _log = new List<string>();
            _fsm = new AsyncStateMachine<Key>();
        }

        private AsyncRecordingState NewState(string name) => new AsyncRecordingState(name, _log);

        [Test]
        public async Task AddState_BeforeStart_IsRegistered()
        {
            _fsm.AddState(Key.A, NewState("A"));

            await _fsm.StartAsync(Key.A);

            Assert.AreEqual(Key.A, _fsm.CurrentKey);
        }

        [Test]
        public async Task AddState_AfterStart_Throws()
        {
            _fsm.AddState(Key.A, NewState("A"));
            await _fsm.StartAsync(Key.A);

            Assert.Throws<InvalidOperationException>(() => _fsm.AddState(Key.B, NewState("B")));
        }

        [Test]
        public void AddState_DuplicateKey_Throws()
        {
            _fsm.AddState(Key.A, NewState("A"));

            Assert.Throws<ArgumentException>(() => _fsm.AddState(Key.A, NewState("A2")));
        }

        [Test]
        public void StartAsync_UnregisteredKey_Throws()
        {
            Assert.ThrowsAsync<ArgumentException>(async () => await _fsm.StartAsync(Key.A));
        }

        [Test]
        public async Task StartAsync_AlreadyStarted_Throws()
        {
            _fsm.AddState(Key.A, NewState("A"));
            await _fsm.StartAsync(Key.A);

            Assert.ThrowsAsync<InvalidOperationException>(async () => await _fsm.StartAsync(Key.A));
        }

        [Test]
        public async Task StartAsync_InitialState_CallsOnEnterAsync()
        {
            var a = NewState("A");
            _fsm.AddState(Key.A, a);

            await _fsm.StartAsync(Key.A);

            Assert.AreEqual(1, a.EnterCount);
            CollectionAssert.AreEqual(new[] { "A:Enter" }, _log);
        }

        [Test]
        public async Task StartAsync_AfterStart_SetsIsStartedAndCurrentKey()
        {
            _fsm.AddState(Key.A, NewState("A"));

            await _fsm.StartAsync(Key.A);

            Assert.IsTrue(_fsm.IsStarted);
            Assert.AreEqual(Key.A, _fsm.CurrentKey);
            Assert.IsNotNull(_fsm.CurrentState);
        }

        [Test]
        public async Task StartAsync_InitialEntry_DoesNotFireStateChanged()
        {
            var fired = false;
            _fsm.AddState(Key.A, NewState("A"));
            _fsm.StateChanged += (_, __) => fired = true;

            await _fsm.StartAsync(Key.A);

            Assert.IsFalse(fired);
        }

        [Test]
        public void CurrentKey_BeforeStart_ReturnsDefault()
        {
            Assert.AreEqual(default(Key), _fsm.CurrentKey);
            Assert.IsNull(_fsm.CurrentState);
        }

        [Test]
        public async Task CurrentKey_AfterStop_ReturnsDefault()
        {
            _fsm.AddState(Key.A, NewState("A"));
            await _fsm.StartAsync(Key.A);
            await _fsm.StopAsync();

            Assert.AreEqual(default(Key), _fsm.CurrentKey);
            Assert.IsNull(_fsm.CurrentState);
        }

        [Test]
        public void IsInState_BeforeStart_ReturnsFalse()
        {
            _fsm.AddState(Key.A, NewState("A"));

            Assert.IsFalse(_fsm.IsInState(Key.A));
        }

        [Test]
        public async Task IsInState_CurrentKey_ReturnsTrue()
        {
            _fsm.AddState(Key.A, NewState("A"));
            _fsm.AddState(Key.B, NewState("B"));
            await _fsm.StartAsync(Key.A);

            Assert.IsTrue(_fsm.IsInState(Key.A));
            Assert.IsFalse(_fsm.IsInState(Key.B));
        }

        [Test]
        public async Task Update_AfterStart_CallsOnUpdateWithDeltaTime()
        {
            var a = NewState("A");
            _fsm.AddState(Key.A, a);
            await _fsm.StartAsync(Key.A);

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
        public async Task StopAsync_AfterStart_CallsOnExitAsyncAndClearsState()
        {
            var a = NewState("A");
            _fsm.AddState(Key.A, a);
            await _fsm.StartAsync(Key.A);

            await _fsm.StopAsync();

            Assert.AreEqual(1, a.ExitCount);
            Assert.IsFalse(_fsm.IsStarted);
            Assert.IsNull(_fsm.CurrentState);
        }

        [Test]
        public async Task StopAsync_WhenNotStarted_IsNoOp()
        {
            await _fsm.StopAsync();

            Assert.IsFalse(_fsm.IsStarted);
        }

        [Test]
        public async Task StartAsync_AfterStop_RestartsCleanly()
        {
            var a = NewState("A");
            _fsm.AddState(Key.A, a);
            await _fsm.StartAsync(Key.A);
            await _fsm.StopAsync();

            await _fsm.StartAsync(Key.A);

            Assert.IsTrue(_fsm.IsStarted);
            Assert.AreEqual(2, a.EnterCount);
            Assert.AreEqual(1, a.ExitCount);
        }

        [Test]
        public void SetStateAsync_BeforeStart_Throws()
        {
            _fsm.AddState(Key.A, NewState("A"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await _fsm.SetStateAsync(Key.A));
        }

        [Test]
        public async Task SetStateAsync_UnregisteredKey_Throws()
        {
            _fsm.AddState(Key.A, NewState("A"));
            await _fsm.StartAsync(Key.A);

            Assert.ThrowsAsync<ArgumentException>(async () => await _fsm.SetStateAsync(Key.B));
        }

        [Test]
        public async Task SetStateAsync_Transition_CallsExitThenEnter()
        {
            _fsm.AddState(Key.A, NewState("A"));
            _fsm.AddState(Key.B, NewState("B"));
            await _fsm.StartAsync(Key.A);
            _log.Clear();

            await _fsm.SetStateAsync(Key.B);

            CollectionAssert.AreEqual(new[] { "A:Exit", "B:Enter" }, _log);
            Assert.AreEqual(Key.B, _fsm.CurrentKey);
        }

        [Test]
        public async Task SetStateAsync_Transition_FiresStateChangedWithPrevAndNextKeys()
        {
            _fsm.AddState(Key.A, NewState("A"));
            _fsm.AddState(Key.B, NewState("B"));
            await _fsm.StartAsync(Key.A);

            Key prev = default, next = default;
            var fireCount = 0;
            _fsm.StateChanged += (p, n) => { prev = p; next = n; fireCount++; };

            await _fsm.SetStateAsync(Key.B);

            Assert.AreEqual(1, fireCount);
            Assert.AreEqual(Key.A, prev);
            Assert.AreEqual(Key.B, next);
        }

        [Test]
        public async Task SetStateAsync_CalledDuringOnEnterAsync_IsDeferred()
        {
            var a = NewState("A");
            var b = NewState("B");
            var c = NewState("C");
            b.OnEnterHook = ct => _fsm.SetStateAsync(Key.C, ct);

            _fsm.AddState(Key.A, a);
            _fsm.AddState(Key.B, b);
            _fsm.AddState(Key.C, c);
            await _fsm.StartAsync(Key.A);
            _log.Clear();

            await _fsm.SetStateAsync(Key.B);

            CollectionAssert.AreEqual(
                new[] { "A:Exit", "B:Enter", "B:Exit", "C:Enter" },
                _log);
            Assert.AreEqual(Key.C, _fsm.CurrentKey);
        }

        [Test]
        public async Task SetStateAsync_CalledDuringOnExitAsync_IsDeferred()
        {
            var a = NewState("A");
            a.OnExitHook = ct => _fsm.SetStateAsync(Key.C, ct);
            var b = NewState("B");
            var c = NewState("C");

            _fsm.AddState(Key.A, a);
            _fsm.AddState(Key.B, b);
            _fsm.AddState(Key.C, c);
            await _fsm.StartAsync(Key.A);
            _log.Clear();

            await _fsm.SetStateAsync(Key.B);

            CollectionAssert.AreEqual(
                new[] { "A:Exit", "B:Enter", "B:Exit", "C:Enter" },
                _log);
            Assert.AreEqual(Key.C, _fsm.CurrentKey);
        }

        [Test]
        public async Task StartAsync_WithSetStateInInitialOnEnter_AppliesDeferredTransition()
        {
            var a = NewState("A");
            a.OnEnterHook = ct => _fsm.SetStateAsync(Key.B, ct);
            var b = NewState("B");

            _fsm.AddState(Key.A, a);
            _fsm.AddState(Key.B, b);

            await _fsm.StartAsync(Key.A);

            CollectionAssert.AreEqual(
                new[] { "A:Enter", "A:Exit", "B:Enter" },
                _log);
            Assert.AreEqual(Key.B, _fsm.CurrentKey);
        }

        [Test]
        public void SetStateAsync_PingPongingOnEnter_ThrowsAtMaxTransitionDepth()
        {
            // Safety cap well above MaxTransitionDepth (16) — fixed code throws long before
            // hitting it; without the cap, broken code would hang the test runner.
            const int safetyCap = 100;
            var bounces = 0;
            var a = NewState("A");
            var b = NewState("B");

            a.OnEnterHook = ct =>
            {
                if (bounces++ < safetyCap)
                    return _fsm.SetStateAsync(Key.B, ct);
                return UniTask.CompletedTask;
            };
            b.OnEnterHook = ct =>
            {
                if (bounces++ < safetyCap)
                    return _fsm.SetStateAsync(Key.A, ct);
                return UniTask.CompletedTask;
            };

            _fsm.AddState(Key.A, a);
            _fsm.AddState(Key.B, b);

            Assert.ThrowsAsync<InvalidOperationException>(async () => await _fsm.StartAsync(Key.A));
        }

        [Test]
        public async Task StartAsync_WithCancellationToken_PassesSameTokenToStateCallbacks()
        {
            var cts = new CancellationTokenSource();
            CancellationToken receivedOnEnter = default;
            CancellationToken receivedOnExit = default;

            var a = new AsyncCallbackState(
                onEnterAsync: ct =>
                {
                    receivedOnEnter = ct;
                    return UniTask.CompletedTask;
                },
                onExitAsync: ct =>
                {
                    receivedOnExit = ct;
                    return UniTask.CompletedTask;
                });
            _fsm.AddState(Key.A, a);

            await _fsm.StartAsync(Key.A, cts.Token);
            await _fsm.StopAsync(cts.Token);

            Assert.AreEqual(cts.Token, receivedOnEnter);
            Assert.AreEqual(cts.Token, receivedOnExit);
        }

        [Test]
        public void GetState_ExistingKeyAndMatchingType_ReturnsState()
        {
            var a = NewState("A");
            _fsm.AddState(Key.A, a);

            Assert.AreSame(a, _fsm.GetState<AsyncRecordingState>(Key.A));
        }

        [Test]
        public void GetState_MismatchedType_ReturnsNull()
        {
            _fsm.AddState(Key.A, NewState("A"));

            Assert.IsNull(_fsm.GetState<OtherAsyncState>(Key.A));
        }

        [Test]
        public void GetState_MissingKey_ReturnsNull()
        {
            Assert.IsNull(_fsm.GetState<AsyncRecordingState>(Key.A));
        }

        [Test]
        public async Task CustomComparer_IsUsedForStateLookupAndIsInState()
        {
            var fsm = new AsyncStateMachine<string>(StringComparer.OrdinalIgnoreCase);
            var state = new AsyncRecordingState("S", new List<string>());
            fsm.AddState("State", state);

            await fsm.StartAsync("STATE");

            Assert.IsTrue(fsm.IsStarted);
            Assert.AreSame(state, fsm.CurrentState);
            Assert.IsTrue(fsm.IsInState("state"));
            Assert.IsTrue(fsm.IsInState("State"));
            Assert.IsTrue(fsm.IsInState("STATE"));
        }

        [Test]
        public async Task SetStateAsync_ToCurrentKey_IsNoOp()
        {
            var a = NewState("A");
            _fsm.AddState(Key.A, a);
            await _fsm.StartAsync(Key.A);
            _log.Clear();

            await _fsm.SetStateAsync(Key.A);

            CollectionAssert.IsEmpty(_log);
            Assert.AreEqual(Key.A, _fsm.CurrentKey);
            Assert.AreEqual(1, a.EnterCount);
            Assert.AreEqual(0, a.ExitCount);
        }

        [Test]
        public async Task SetStateAsync_MultipleCallsDuringOnEnter_LastWriteWins()
        {
            var a = NewState("A");
            var b = NewState("B");
            var c = NewState("C");
            var d = NewState("D");
            b.OnEnterHook = ct =>
            {
                _ = _fsm.SetStateAsync(Key.C, ct);
                _ = _fsm.SetStateAsync(Key.D, ct);
                return UniTask.CompletedTask;
            };

            _fsm.AddState(Key.A, a);
            _fsm.AddState(Key.B, b);
            _fsm.AddState(Key.C, c);
            _fsm.AddState(Key.D, d);
            await _fsm.StartAsync(Key.A);
            _log.Clear();

            await _fsm.SetStateAsync(Key.B);

            CollectionAssert.AreEqual(
                new[] { "A:Exit", "B:Enter", "B:Exit", "D:Enter" },
                _log);
            Assert.AreEqual(Key.D, _fsm.CurrentKey);
            Assert.AreEqual(0, c.EnterCount);
        }

        [Test]
        public async Task SetStateAsync_CalledDuringOnEnter_UsesDeferredCallerToken()
        {
            var cts1 = new CancellationTokenSource();
            var cts2 = new CancellationTokenSource();
            CancellationToken receivedAtC = default;

            _fsm.AddState(Key.A, NewState("A"));
            _fsm.AddState(Key.B, new AsyncCallbackState(onEnterAsync: ct =>
            {
                _ = _fsm.SetStateAsync(Key.C, cts2.Token);
                return UniTask.CompletedTask;
            }));
            _fsm.AddState(Key.C, new AsyncCallbackState(onEnterAsync: ct =>
            {
                receivedAtC = ct;
                return UniTask.CompletedTask;
            }));

            await _fsm.StartAsync(Key.A);
            await _fsm.SetStateAsync(Key.B, cts1.Token);

            Assert.AreEqual(cts2.Token, receivedAtC);
            Assert.AreNotEqual(cts1.Token, receivedAtC);
        }

        [Test]
        public async Task SetStateAsync_CancelledDuringOnEnterAwait_ThrowsOperationCanceledException()
        {
            var cts = new CancellationTokenSource();

            _fsm.AddState(Key.A, NewState("A"));
            _fsm.AddState(Key.B, new AsyncCallbackState(onEnterAsync: ct =>
            {
                var tcs = new UniTaskCompletionSource();
                ct.Register(() => tcs.TrySetCanceled());
                return tcs.Task;
            }));
            await _fsm.StartAsync(Key.A);

            var transition = _fsm.SetStateAsync(Key.B, cts.Token);
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await transition);
        }

        [Test]
        public async Task SetStateAsync_CancelledDuringOnExitAwait_ThrowsOperationCanceledException()
        {
            var cts = new CancellationTokenSource();

            _fsm.AddState(Key.A, new AsyncCallbackState(onExitAsync: ct =>
            {
                var tcs = new UniTaskCompletionSource();
                ct.Register(() => tcs.TrySetCanceled());
                return tcs.Task;
            }));
            _fsm.AddState(Key.B, NewState("B"));
            await _fsm.StartAsync(Key.A);

            var transition = _fsm.SetStateAsync(Key.B, cts.Token);
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await transition);
        }

        [Test]
        public async Task SetStateAsync_CancelledDuringOnEnter_FsmRecoversAndAcceptsNextTransition()
        {
            var cts = new CancellationTokenSource();
            var c = NewState("C");

            _fsm.AddState(Key.A, NewState("A"));
            _fsm.AddState(Key.B, new AsyncCallbackState(onEnterAsync: ct =>
            {
                var tcs = new UniTaskCompletionSource();
                ct.Register(() => tcs.TrySetCanceled());
                return tcs.Task;
            }));
            _fsm.AddState(Key.C, c);
            await _fsm.StartAsync(Key.A);

            var transition = _fsm.SetStateAsync(Key.B, cts.Token);
            cts.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await transition);

            Assert.IsFalse(_fsm.HasPendingTransition);
            await _fsm.SetStateAsync(Key.C);

            Assert.AreEqual(Key.C, _fsm.CurrentKey);
            Assert.AreEqual(1, c.EnterCount);
        }

        [Test]
        public async Task SetStateAsync_CancelledDuringOnExit_FsmRecoversAndAcceptsNextTransition()
        {
            var cts = new CancellationTokenSource();
            var c = NewState("C");

            // The cancelled transition leaves the FSM still in A (OnExit aborted before the
            // state advanced), so the recovery transition to C must exit A a second time. A's
            // first exit blocks until cancelled; every later exit completes normally — otherwise
            // the recovery exit (given a non-cancellable token) would hang forever.
            int exitCalls = 0;
            _fsm.AddState(Key.A, new AsyncCallbackState(onExitAsync: ct =>
            {
                if (++exitCalls > 1)
                    return UniTask.CompletedTask;

                var tcs = new UniTaskCompletionSource();
                ct.Register(() => tcs.TrySetCanceled());
                return tcs.Task;
            }));
            _fsm.AddState(Key.B, NewState("B"));
            _fsm.AddState(Key.C, c);
            await _fsm.StartAsync(Key.A);

            var transition = _fsm.SetStateAsync(Key.B, cts.Token);
            cts.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await transition);

            Assert.IsFalse(_fsm.HasPendingTransition);
            await _fsm.SetStateAsync(Key.C);

            Assert.AreEqual(Key.C, _fsm.CurrentKey);
            Assert.AreEqual(1, c.EnterCount);
        }

        [Test]
        public async Task StartAsync_CancelledDuringInitialOnEnter_FsmCanBeStoppedAndRestarted()
        {
            var cts = new CancellationTokenSource();
            var b = NewState("B");

            _fsm.AddState(Key.A, new AsyncCallbackState(onEnterAsync: ct =>
            {
                var tcs = new UniTaskCompletionSource();
                ct.Register(() => tcs.TrySetCanceled());
                return tcs.Task;
            }));
            _fsm.AddState(Key.B, b);

            var start = _fsm.StartAsync(Key.A, cts.Token);
            cts.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await start);

            await _fsm.StopAsync();
            await _fsm.StartAsync(Key.B);

            Assert.AreEqual(Key.B, _fsm.CurrentKey);
            Assert.AreEqual(1, b.EnterCount);
        }

        [Test]
        public async Task StopAsync_CancelledDuringOnExitAwait_PropagatesCancellation()
        {
            var cts = new CancellationTokenSource();

            _fsm.AddState(Key.A, new AsyncCallbackState(onExitAsync: ct =>
            {
                var tcs = new UniTaskCompletionSource();
                ct.Register(() => tcs.TrySetCanceled());
                return tcs.Task;
            }));
            await _fsm.StartAsync(Key.A);

            var stop = _fsm.StopAsync(cts.Token);
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await stop);
        }

        [Test]
        public async Task HasPendingTransition_DuringOnEnter_ReflectsQueuedKey()
        {
            var a = NewState("A");
            var b = NewState("B");
            var c = NewState("C");
            bool pendingDuringEnter = false;
            Key pendingKeyDuringEnter = default;
            b.OnEnterHook = ct =>
            {
                _ = _fsm.SetStateAsync(Key.C, ct);
                pendingDuringEnter = _fsm.HasPendingTransition;
                pendingKeyDuringEnter = _fsm.PendingKey;
                return UniTask.CompletedTask;
            };

            _fsm.AddState(Key.A, a);
            _fsm.AddState(Key.B, b);
            _fsm.AddState(Key.C, c);
            await _fsm.StartAsync(Key.A);

            await _fsm.SetStateAsync(Key.B);

            Assert.IsTrue(pendingDuringEnter);
            Assert.AreEqual(Key.C, pendingKeyDuringEnter);
            Assert.IsFalse(_fsm.HasPendingTransition);
        }

        [Test]
        public async Task GetCurrentState_ReturnsCurrentStateAsType()
        {
            var a = NewState("A");
            _fsm.AddState(Key.A, a);
            await _fsm.StartAsync(Key.A);

            Assert.AreSame(a, _fsm.GetCurrentState<AsyncRecordingState>());
            Assert.IsNull(_fsm.GetCurrentState<OtherAsyncState>());
        }

        private class AsyncRecordingState : AsyncStateBase
        {
            public readonly string Name;
            public readonly List<string> Log;
            public int EnterCount;
            public int UpdateCount;
            public int ExitCount;
            public float LastDelta = -1f;
            public Func<CancellationToken, UniTask> OnEnterHook;
            public Func<CancellationToken, UniTask> OnExitHook;

            public AsyncRecordingState(string name, List<string> log)
            {
                Name = name;
                Log = log;
            }

            public override UniTask OnEnterAsync(CancellationToken ct)
            {
                EnterCount++;
                Log.Add(Name + ":Enter");
                return OnEnterHook?.Invoke(ct) ?? UniTask.CompletedTask;
            }

            public override void OnUpdate(float deltaTime)
            {
                UpdateCount++;
                LastDelta = deltaTime;
                Log.Add(Name + ":Update");
            }

            public override UniTask OnExitAsync(CancellationToken ct)
            {
                ExitCount++;
                Log.Add(Name + ":Exit");
                return OnExitHook?.Invoke(ct) ?? UniTask.CompletedTask;
            }
        }

        private class OtherAsyncState : AsyncStateBase { }
    }
}
