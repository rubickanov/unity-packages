using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Loading
{
    /// <summary>
    /// Implemented by <see cref="ILoadingOperation"/>s that split work into two phases:
    /// an initial load (via <see cref="ILoadingOperation.Execute"/>) and a deferred activation.
    /// <see cref="LoadingService"/> calls <see cref="Activate"/> after all operations have
    /// executed and after any user-input gate has been passed.
    /// </summary>
    public interface IDeferrableOperation : ILoadingOperation
    {
        /// <summary>
        /// Called after all operations' <see cref="ILoadingOperation.Execute"/> have finished
        /// (and after any <c>waitForInput</c> gate has been passed). Activations run in
        /// operation-list order — a failed activation aborts the pipeline but does NOT roll
        /// back already-activated operations; implementations must be safe to that partial state.
        /// </summary>
        UniTask Activate(CancellationToken ct);
    }
}
