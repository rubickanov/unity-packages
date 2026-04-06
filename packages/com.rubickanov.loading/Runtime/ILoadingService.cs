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
        /// Returns <see cref="LoadResult"/> indicating success or failure with the exception.
        /// </summary>
        /// <param name="operations">Ordered list of operations to execute sequentially.</param>
        /// <param name="ct">Cancellation token.</param>
        UniTask<LoadResult> Load(
            IReadOnlyList<ILoadingOperation> operations,
            bool waitForInput = false,
            CancellationToken ct = default);
    }
}
