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
            var vm = new DynamicDialogViewModel(title) { Message = message };
            _ui.Show<DynamicPopup>(vm).Forget();
            return new ModalHandle(_ui);
        }

        public DialogBuilder CreateDialog(string title)
        {
            return new DialogBuilder(title, ShowDynamicAsync);
        }

        private async UniTask<DialogResult> ShowDynamicAsync(DynamicDialogViewModel vm)
        {
            await _ui.Show<DynamicPopup>(vm);
            var result = await vm.Result;
            await _ui.HideAsync<DynamicPopup>();
            return result;
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
                _ui.Hide<DynamicPopup>();
            }
        }
    }
}
