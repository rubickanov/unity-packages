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
        /// </summary>
        /// <param name="operations">Ordered list of operations to execute sequentially.</param>
        /// <param name="ct">Cancellation token.</param>
        UniTask Load(IReadOnlyList<ILoadingOperation> operations, CancellationToken ct = default);
    }
}
