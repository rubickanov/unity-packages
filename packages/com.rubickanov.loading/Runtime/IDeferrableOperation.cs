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
    public interface IDeferrableOperation
    {
        UniTask Activate(CancellationToken ct);
    }
}
