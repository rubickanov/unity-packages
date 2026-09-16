using System;
using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public class DialogService : IDialogService
    {
        private readonly IPopupService _popups;

        public DialogService(IPopupService popups)
        {
            _popups = popups ?? throw new ArgumentNullException(nameof(popups));
        }

        public async UniTask<bool> ShowConfirm(string title, string message,
            string confirmText = "Yes", string cancelText = "No")
        {
            var result = await CreateDialog(title)
                .WithMessage(message)
                .AddButton(confirmText, "confirm", isPrimary: true)
                .AddButton(cancelText, "cancel")
                .ShowAsync();

            return result.ButtonId == "confirm";
        }

        public async UniTask ShowAlert(string title, string message, string buttonText = "OK")
        {
            await CreateDialog(title)
                .WithMessage(message)
                .AddButton(buttonText, "ok", isPrimary: true)
                .ShowAsync();
        }

        public IDisposable ShowModal(string title, string message)
        {
            var handle = _popups.Create()
                .Title(title)
                .Message(message)
                .Modal()
                .Open();
            return new ModalHandle(handle);
        }

        public DialogBuilder CreateDialog(string title) => new(title, _popups);

        private sealed class ModalHandle : IDisposable
        {
            private readonly IPopupHandle _handle;
            private bool _disposed;

            public ModalHandle(IPopupHandle handle) => _handle = handle;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _handle.Close();
            }
        }
    }
}
