using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Loading
{
    /// <summary>
    /// Abstraction for presenting loading progress to the user.
    /// Decouples the loading pipeline from any specific UI implementation.
    /// </summary>
    public interface ILoadingPresenter
    {
        /// <summary>
        /// Shows the loading UI. The returned <see cref="UniTask"/> is awaited <i>in parallel</i>
        /// with the first operations — implementations MUST accept <see cref="SetProgress"/> /
        /// <see cref="SetDescription"/> calls before this task completes.
        /// </summary>
        UniTask Show();

        /// <summary>Updates the progress bar (0–1).</summary>
        void SetProgress(float progress);

        /// <summary>Updates the status description text.</summary>
        void SetDescription(string description);

        /// <summary>
        /// Displays an error message. Called by <see cref="LoadingService"/> when a pipeline
        /// operation throws, before <see cref="Hide"/>.
        /// </summary>
        void SetError(string error);

        /// <summary>Waits for user input before proceeding.</summary>
        UniTask WaitForInput(CancellationToken ct = default);

        /// <summary>
        /// Hides the loading UI. May be called without a preceding <see cref="Show"/> — the
        /// service issues a defensive <see cref="Hide"/> at the start of each <c>Load</c> to
        /// clear any stale state; implementations must be idempotent.
        /// </summary>
        UniTask Hide();
    }
}
