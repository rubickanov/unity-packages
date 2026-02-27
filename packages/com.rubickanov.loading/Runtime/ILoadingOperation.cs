using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Loading
{
    /// <summary>
    /// A single async operation executed as part of the loading pipeline.
    /// </summary>
    public interface ILoadingOperation
    {
        /// <summary>
        /// Human-readable description shown in the loading UI.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Executes the operation, reporting normalized progress (0–1).
        /// </summary>
        /// <param name="progress">Progress reporter (0–1).</param>
        /// <param name="ct">Cancellation token.</param>
        UniTask Execute(IProgress<float> progress, CancellationToken ct);
    }
}
