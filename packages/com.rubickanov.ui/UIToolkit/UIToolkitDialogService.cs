using System;
using Cysharp.Threading.Tasks;

namespace Rubickanov.UI.UIToolkit
{
    public class UIToolkitDialogService : IDialogService
    {
        private readonly IUIService _ui;

        public UIToolkitDialogService(IUIService ui)
        {
            _ui = ui;
        }

        public async UniTask<bool> ShowConfirm(string title, string message,
            string confirmText = "Yes", string cancelText = "No")
        {
            var vm = new ConfirmViewModel(title, message, confirmText, cancelText);
            await _ui.ShowPopup<ConfirmPopup>(vm);
            bool result = await vm.Result;
            await _ui.HidePopupAsync<ConfirmPopup>();
            return result;
        }

        public async UniTask ShowAlert(string title, string message, string buttonText = "OK")
        {
            var vm = new AlertViewModel(title, message, buttonText);
            await _ui.ShowPopup<AlertPopup>(vm);
            await vm.Result;
            await _ui.HidePopupAsync<AlertPopup>();
        }

        public IDisposable ShowModal(string title, string message)
        {
            var vm = new AlertViewModel(title, message, "");
            _ui.ShowPopup<AlertPopup>(vm).Forget();
            return new ModalHandle(_ui);
        }

        private sealed class ModalHandle : IDisposable
        {
            private readonly IUIService _ui;
            private bool _disposed;

            public ModalHandle(IUIService ui) => _ui = ui;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _ui.HidePopup<AlertPopup>();
            }
        }
    }
}
