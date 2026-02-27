using System;
using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public class NullDialogService : IDialogService
    {
        public UniTask<bool> ShowConfirm(string title, string message,
            string confirmText = "Yes", string cancelText = "No") => UniTask.FromResult(false);

        public UniTask ShowAlert(string title, string message, string buttonText = "OK") => UniTask.CompletedTask;

        public IDisposable ShowModal(string title, string message) => new NoOpDisposable();

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
