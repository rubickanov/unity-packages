using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Loading.Tests
{
    /// <summary>
    /// Test-only <see cref="ILoadingOperation"/> with configurable behavior. Ordering across
    /// multiple operations is observed by passing a shared <see cref="List{String}"/> log
    /// into the constructor — the operation appends its description when it runs.
    /// </summary>
    internal class FakeOperation : ILoadingOperation
    {
        private readonly Func<FakeOperation, IProgress<float>, CancellationToken, UniTask> _executeBody;
        private readonly List<string>? _executionLog;

        public string Description { get; }
        public bool Executed { get; private set; }

        public FakeOperation(string description = "fake-op", List<string>? executionLog = null)
            : this(description, DefaultBody, executionLog)
        {
        }

        protected FakeOperation(
            string description,
            Func<FakeOperation, IProgress<float>, CancellationToken, UniTask> executeBody,
            List<string>? executionLog = null)
        {
            Description = description;
            _executeBody = executeBody;
            _executionLog = executionLog;
        }

        public async UniTask Execute(IProgress<float> progress, CancellationToken ct)
        {
            Executed = true;
            _executionLog?.Add(Description);
            await _executeBody(this, progress, ct);
        }

        private static UniTask DefaultBody(FakeOperation self, IProgress<float> progress, CancellationToken ct)
        {
            progress.Report(0f);
            progress.Report(1f);
            return UniTask.CompletedTask;
        }

        /// <summary>Reports the given progress values in order, then completes.</summary>
        public static FakeOperation ReportingProgress(string description, params float[] values)
        {
            return new FakeOperation(description, (_, progress, _) =>
            {
                foreach (var v in values)
                    progress.Report(v);
                return UniTask.CompletedTask;
            });
        }

        /// <summary>Throws the given exception synchronously when executed.</summary>
        public static FakeOperation Throwing(string description, Exception ex)
        {
            return new FakeOperation(description, (_, _, _) => throw ex);
        }

        /// <summary>Awaits forever; completes (with cancellation) when the token is cancelled.</summary>
        public static FakeOperation WaitingForever(string description = "wait-forever")
        {
            return new FakeOperation(description, (_, _, ct) => UniTask.Never(ct));
        }
    }
}
