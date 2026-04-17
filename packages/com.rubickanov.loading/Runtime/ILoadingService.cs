using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Loading
{
    /// <summary>
    /// Generic loading pipeline that executes a sequence of <see cref="ILoadingOperation"/>s
    /// with progress tracking.
    /// </summary>
    public interface ILoadingService
    {
        /// <summary>
        /// Executes a sequence of loading operations, reporting progress via the presenter.
        /// Returns <see cref="LoadResult"/> indicating success, cancellation, or failure.
        /// <para>
        /// Operations run in list order. If any <see cref="ILoadingOperation.Execute"/> or
        /// <see cref="IDeferrableOperation.Activate"/> throws, the pipeline aborts at that
        /// point — deferred activations that already ran are NOT rolled back; callers and
        /// operation authors must keep activations safe against this partial-state scenario.
        /// </para>
        /// </summary>
        /// <param name="operations">Ordered list of operations to execute sequentially.</param>
        /// <param name="waitForInput">When true, waits for user input before activating deferred operations.</param>
        /// <param name="ct">Cancellation token.</param>
        UniTask<LoadResult> Load(
            IReadOnlyList<ILoadingOperation> operations,
            bool waitForInput = false,
            CancellationToken ct = default);
    }
}
