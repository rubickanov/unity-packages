using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Loading.Tests
{
    /// <summary>
    /// In-memory <see cref="ILoadingPresenter"/> that records every call for assertion in tests.
    /// </summary>
    internal sealed class RecordingPresenter : ILoadingPresenter
    {
        public readonly List<string> Calls = new();
        public readonly List<float> ProgressValues = new();
        public readonly List<string> Descriptions = new();
        public readonly List<string> Errors = new();

        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public int WaitForInputCount { get; private set; }

        /// <summary>
        /// When set, <see cref="WaitForInput"/> awaits this source instead of returning
        /// a completed task. Tests can assert the pipeline is paused here, then release it.
        /// </summary>
        public UniTaskCompletionSource? WaitForInputGate;

        public UniTask Show()
        {
            ShowCount++;
            Calls.Add("Show");
            return UniTask.CompletedTask;
        }

        public void SetProgress(float progress)
        {
            ProgressValues.Add(progress);
            Calls.Add("SetProgress:" + progress.ToString("0.######", CultureInfo.InvariantCulture));
        }

        public void SetDescription(string description)
        {
            Descriptions.Add(description);
            Calls.Add("SetDescription:" + description);
        }

        public void SetError(string error)
        {
            Errors.Add(error);
            Calls.Add("SetError:" + error);
        }

        public UniTask WaitForInput(CancellationToken ct = default)
        {
            WaitForInputCount++;
            Calls.Add("WaitForInput");
            if (WaitForInputGate != null)
                return WaitForInputGate.Task;
            return UniTask.CompletedTask;
        }

        public UniTask Hide()
        {
            HideCount++;
            Calls.Add("Hide");
            return UniTask.CompletedTask;
        }
    }
}
