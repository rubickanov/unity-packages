using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Rubickanov.Loading.Tests
{
    [TestFixture]
    public class LoadingServiceTests
    {
        private RecordingPresenter _presenter = null!;
        private LoadingService _service = null!;

        [SetUp]
        public void SetUp()
        {
            _presenter = new RecordingPresenter();
            _service = new LoadingService(_presenter, NullLoggerFactory.Instance);
        }

        [Test]
        public async Task Load_EmptyOperations_ReturnsOkWithoutTouchingPresenter()
        {
            var result = await _service.Load(Array.Empty<ILoadingOperation>());

            Assert.IsTrue(result.Success);
            Assert.IsNull(result.Error);
            Assert.AreEqual(0, _presenter.ShowCount);
            Assert.AreEqual(0, _presenter.HideCount);
            Assert.IsEmpty(_presenter.ProgressValues);
        }

        [Test]
        public async Task Load_MultipleOperations_ExecutesInListOrder()
        {
            var log = new List<string>();
            var ops = new ILoadingOperation[]
            {
                new FakeOperation("a", log),
                new FakeOperation("b", log),
                new FakeOperation("c", log),
            };

            await _service.Load(ops);

            Assert.AreEqual(new[] { "a", "b", "c" }, log);
        }

        [Test]
        public async Task Load_MultipleOperations_PushesEachDescriptionToPresenter()
        {
            var ops = new ILoadingOperation[]
            {
                new FakeOperation("loading-a"),
                new FakeOperation("loading-b"),
            };

            await _service.Load(ops);

            // LoadingService sets its own "Loading..." default first, then each op's Description
            // is pushed before its Execute runs.
            Assert.AreEqual("Loading...", _presenter.Descriptions[0]);
            Assert.Less(
                _presenter.Descriptions.IndexOf("loading-a"),
                _presenter.Descriptions.IndexOf("loading-b"));
        }

        [Test]
        public async Task Load_HappyPath_CallsShowOnceAndHideTwice()
        {
            // Contract: Hide fires once at the start (guards against a stale leftover UI from a
            // prior aborted Load) and once in the finally block after the pipeline completes.
            await _service.Load(new ILoadingOperation[] { new FakeOperation() });

            Assert.AreEqual(1, _presenter.ShowCount);
            Assert.AreEqual(2, _presenter.HideCount);
        }

        [Test]
        public async Task Load_HappyPath_ReturnsOk()
        {
            var result = await _service.Load(new ILoadingOperation[] { new FakeOperation() });

            Assert.IsTrue(result.Success);
            Assert.IsNull(result.Error);
        }

        [Test]
        public async Task Load_TwoOperationsEachReportingHalf_ScalesIntoHalfSlices()
        {
            var ops = new ILoadingOperation[]
            {
                FakeOperation.ReportingProgress("a", 0.5f),
                FakeOperation.ReportingProgress("b", 0.5f),
            };

            await _service.Load(ops);

            // Expected stream: 0 (init) → 0.25 (op0 half of first half) → 0.75 (op1 half of second half) → 1 (final).
            AssertContainsApprox(_presenter.ProgressValues, 0f);
            AssertContainsApprox(_presenter.ProgressValues, 0.25f);
            AssertContainsApprox(_presenter.ProgressValues, 0.75f);
            Assert.AreEqual(1f, _presenter.ProgressValues[^1]);
        }

        [Test]
        public async Task Load_ThreeOperationsEachReportingFull_FillsEachThirdSliceCompletely()
        {
            var ops = new ILoadingOperation[]
            {
                FakeOperation.ReportingProgress("a", 1f),
                FakeOperation.ReportingProgress("b", 1f),
                FakeOperation.ReportingProgress("c", 1f),
            };

            await _service.Load(ops);

            AssertContainsApprox(_presenter.ProgressValues, 1f / 3f);
            AssertContainsApprox(_presenter.ProgressValues, 2f / 3f);
            AssertContainsApprox(_presenter.ProgressValues, 1f);
        }

        [Test]
        public async Task Load_WithPartialProgressReports_StillEndsAtOne()
        {
            var ops = new ILoadingOperation[]
            {
                FakeOperation.ReportingProgress("a", 0.1f),
                FakeOperation.ReportingProgress("b", 0.2f),
            };

            await _service.Load(ops);

            Assert.AreEqual(1f, _presenter.ProgressValues[^1]);
        }

        [Test]
        public async Task Load_DeferrableOperation_ActivatesAfterAllExecutes()
        {
            var executionLog = new List<string>();
            var activationLog = new List<string>();
            var defOp = new DeferrableFakeOperation("deferred", executionLog, activationLog);
            var tailOp = new FakeOperation("tail", executionLog);

            await _service.Load(new ILoadingOperation[] { defOp, tailOp });

            Assert.IsTrue(defOp.Activated);
            Assert.AreEqual(new[] { "deferred", "tail" }, executionLog);
            Assert.AreEqual(new[] { "deferred" }, activationLog);
        }

        [Test]
        public async Task Load_MultipleDeferrables_ActivatesInListOrder()
        {
            var activationLog = new List<string>();
            var ops = new ILoadingOperation[]
            {
                new DeferrableFakeOperation("first", activationLog: activationLog),
                new DeferrableFakeOperation("second", activationLog: activationLog),
            };

            await _service.Load(ops);

            Assert.AreEqual(new[] { "first", "second" }, activationLog);
        }

        [Test]
        public async Task Load_NonDeferrableOperation_CompletesWithoutActivation()
        {
            var op = new FakeOperation("plain");

            var result = await _service.Load(new ILoadingOperation[] { op });

            Assert.IsTrue(result.Success);
            Assert.IsTrue(op.Executed);
        }

        [Test]
        public async Task Load_WaitForInputFalse_DoesNotCallWaitForInput()
        {
            await _service.Load(
                new ILoadingOperation[] { new FakeOperation() },
                waitForInput: false);

            Assert.AreEqual(0, _presenter.WaitForInputCount);
        }

        [Test]
        public async Task Load_WaitForInputTrue_GatesActivationUntilInputReleased()
        {
            _presenter.WaitForInputGate = new UniTaskCompletionSource();
            var defOp = new DeferrableFakeOperation("deferred");

            var loadTask = _service.Load(
                new ILoadingOperation[] { defOp },
                waitForInput: true);

            Assert.AreEqual(1, _presenter.WaitForInputCount);
            Assert.IsTrue(defOp.Executed);
            Assert.IsFalse(defOp.Activated, "Activate must not run until WaitForInput resolves.");

            _presenter.WaitForInputGate.TrySetResult();
            var result = await loadTask;

            Assert.IsTrue(result.Success);
            Assert.IsTrue(defOp.Activated);
        }

        [Test]
        public async Task Load_PreCancelledToken_ReturnsCancelled()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await _service.Load(
                new ILoadingOperation[] { new FakeOperation() },
                ct: cts.Token);

            Assert.IsTrue(result.Cancelled);
            Assert.IsNull(result.Error);
        }

        [Test]
        public async Task Load_TokenCancelledDuringExecute_ReturnsCancelledAndSkipsRemainingOperations()
        {
            using var cts = new CancellationTokenSource();
            var blocker = FakeOperation.WaitingForever("blocker");
            var after = new FakeOperation("after");

            var loadTask = _service.Load(
                new ILoadingOperation[] { blocker, after },
                ct: cts.Token);

            Assert.IsTrue(blocker.Executed);
            Assert.IsFalse(after.Executed);

            cts.Cancel();
            var result = await loadTask;

            Assert.IsTrue(result.Cancelled);
            Assert.IsFalse(after.Executed);
        }

        [Test]
        public async Task Load_TokenCancelledDuringExecute_DoesNotActivateDeferrables()
        {
            using var cts = new CancellationTokenSource();
            var blocker = FakeOperation.WaitingForever("blocker");
            var defOp = new DeferrableFakeOperation("deferred");

            var loadTask = _service.Load(
                new ILoadingOperation[] { blocker, defOp },
                ct: cts.Token);

            cts.Cancel();
            await loadTask;

            Assert.IsFalse(defOp.Activated);
        }

        [Test]
        public async Task Load_OperationThrows_ReturnsFailCarryingException()
        {
            var ex = new InvalidOperationException("boom");
            var throwing = FakeOperation.Throwing("bad", ex);

            var result = await _service.Load(new ILoadingOperation[] { throwing });

            Assert.IsFalse(result.Success);
            Assert.AreSame(ex, result.Error);
        }

        [Test]
        public async Task Load_OperationThrows_SkipsSubsequentOperations()
        {
            var throwing = FakeOperation.Throwing("bad", new Exception("x"));
            var after = new FakeOperation("after");

            await _service.Load(new ILoadingOperation[] { throwing, after });

            Assert.IsFalse(after.Executed);
        }

        [Test]
        public async Task Load_OperationThrows_DoesNotActivateDeferrables()
        {
            var throwing = FakeOperation.Throwing("bad", new Exception("x"));
            var defOp = new DeferrableFakeOperation("deferred");

            await _service.Load(new ILoadingOperation[] { throwing, defOp });

            Assert.IsFalse(defOp.Activated);
        }

        [Test]
        public async Task Load_OperationThrows_CallsSetErrorOnPresenter()
        {
            var ex = new InvalidOperationException("boom");
            var throwing = FakeOperation.Throwing("bad", ex);

            await _service.Load(new ILoadingOperation[] { throwing });

            Assert.Contains("boom", _presenter.Errors);
        }

        [Test]
        public async Task Load_OperationThrows_StillHidesPresenter()
        {
            var throwing = FakeOperation.Throwing("bad", new Exception("x"));

            await _service.Load(new ILoadingOperation[] { throwing });

            // At least the initial Hide (line 40) plus the finally Hide (line 69).
            Assert.GreaterOrEqual(_presenter.HideCount, 2);
        }

        [Test]
        public async Task Load_StartedWhileEarlierLoadStillRunning_CancelsEarlierLoadAndBothReturnOk()
        {
            var blocker = FakeOperation.WaitingForever("blocker");
            var firstTask = _service.Load(new ILoadingOperation[] { blocker });

            Assert.IsTrue(blocker.Executed, "first load should have entered its first operation.");

            var secondResult = await _service.Load(new ILoadingOperation[] { new FakeOperation("second") });
            var firstResult = await firstTask;

            // Reentry cancel (first load was cancelled by second, not by external ct) is Ok, not Cancelled.
            Assert.IsTrue(firstResult.Success, "reentry-cancelled first load must return Ok.");
            Assert.IsTrue(secondResult.Success);
            Assert.AreEqual(2, _presenter.ShowCount);
        }

        [Test]
        public async Task Load_OperationReportsProgressAfterItCompletes_LateReportIsIgnored()
        {
            IProgress<float>? capturedFromFirst = null;
            var first = FakeOperation.CapturingProgress("first", p => capturedFromFirst = p);
            var second = FakeOperation.ReportingProgress("second", 0f);

            await _service.Load(new ILoadingOperation[] { first, second });
            var lengthAfterRun = _presenter.ProgressValues.Count;

            capturedFromFirst!.Report(1f);

            Assert.AreEqual(
                lengthAfterRun,
                _presenter.ProgressValues.Count,
                "Late progress.Report from a completed operation must not reach the presenter.");
        }

        private static void AssertContainsApprox(
            IList<float> values,
            float expected,
            float tolerance = 0.0001f)
        {
            foreach (var v in values)
            {
                if (Math.Abs(v - expected) <= tolerance)
                    return;
            }

            Assert.Fail(
                $"Expected progress stream to contain a value ≈ {expected}. "
                + $"Actual: [{string.Join(", ", values)}]");
        }
    }
}
