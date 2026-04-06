using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Loading
{
    /// <summary>
    /// No-op <see cref="ILoadingPresenter"/> for headless or server builds
    /// where no loading UI is needed.
    /// </summary>
    public class NullLoadingPresenter : ILoadingPresenter
    {
        /// <inheritdoc />
        public UniTask Show() => UniTask.CompletedTask;

        /// <inheritdoc />
        public void SetProgress(float progress) { }

        /// <inheritdoc />
        public void SetDescription(string description) { }

        /// <inheritdoc />
        public void SetError(string error) { }

        /// <inheritdoc />
        public UniTask WaitForInput(CancellationToken ct = default) => UniTask.CompletedTask;

        /// <inheritdoc />
        public UniTask Hide() => UniTask.CompletedTask;
    }
}
