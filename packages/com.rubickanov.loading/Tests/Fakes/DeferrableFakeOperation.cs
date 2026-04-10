using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Loading.Tests
{
    /// <summary>
    /// <see cref="FakeOperation"/> that also implements <see cref="IDeferrableOperation"/>.
    /// Tests can pass a separate <c>activationLog</c> to observe activation order.
    /// </summary>
    internal sealed class DeferrableFakeOperation : FakeOperation, IDeferrableOperation
    {
        private readonly List<string>? _activationLog;

        public bool Activated { get; private set; }

        public DeferrableFakeOperation(
            string description = "deferrable-op",
            List<string>? executionLog = null,
            List<string>? activationLog = null)
            : base(description, executionLog)
        {
            _activationLog = activationLog;
        }

        public UniTask Activate(CancellationToken ct)
        {
            Activated = true;
            _activationLog?.Add(Description);
            return UniTask.CompletedTask;
        }
    }
}
