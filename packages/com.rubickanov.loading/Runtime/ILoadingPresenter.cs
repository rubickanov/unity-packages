using Cysharp.Threading.Tasks;

namespace Rubickanov.Loading
{
    /// <summary>
    /// Abstraction for presenting loading progress to the user.
    /// Decouples the loading pipeline from any specific UI implementation.
    /// </summary>
    public interface ILoadingPresenter
    {
        /// <summary>Shows the loading UI.</summary>
        UniTask Show();

        /// <summary>Updates the progress bar (0–1).</summary>
        void SetProgress(float progress);

        /// <summary>Updates the status description text.</summary>
        void SetDescription(string description);

        /// <summary>Displays an error message.</summary>
        void SetError(string error);

        /// <summary>Hides the loading UI.</summary>
        void Hide();
    }
}
