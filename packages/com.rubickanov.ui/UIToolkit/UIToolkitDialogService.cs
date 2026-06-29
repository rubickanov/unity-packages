using System;
using Cysharp.Threading.Tasks;

namespace Rubickanov.UI.UIToolkit
{
    public class UIToolkitDialogService : IDialogService
    {
        private readonly IUIService _ui;
        private readonly IPopupService? _popups;

        public UIToolkitDialogService(IUIService ui, IPopupService? popups = null)
        {
            _ui = ui;
            _popups = popups;
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
            if (_popups != null)
            {
                var handle = _popups.Create()
                    .Title(title)
                    .Message(message)
                    .Modal()
                    .Open();
                return new PopupModalHandle(handle);
            }

            var vm = new DynamicDialogViewModel(title) { Message = message };
            _ui.Show<DynamicPopup>(vm).Forget();
            return new ModalHandle(_ui);
        }

        public DialogBuilder CreateDialog(string title)
        {
            return new DialogBuilder(title, _popups != null ? ShowViaPopupAsync : ShowDynamicAsync);
        }

        private async UniTask<DialogResult> ShowDynamicAsync(DynamicDialogViewModel vm)
        {
            await _ui.Show<DynamicPopup>(vm);
            var result = await vm.Result;
            await _ui.HideAsync<DynamicPopup>();
            return result;
        }

        // Routes the legacy dialog model through the flexible popup engine (centered modal),
        // preserving DialogBuilder / IDialogService behaviour.
        private async UniTask<DialogResult> ShowViaPopupAsync(DynamicDialogViewModel vm)
        {
            var config = new PopupConfig
            {
                Title = vm.Title,
                Message = vm.Message,
                Icon = vm.Image,
                ContentFactory = vm.ContentFactory,
                HasInput = vm.HasInput,
                InputPlaceholder = vm.InputPlaceholder,
                InputDefault = vm.InputDefault,
                Behaviour = PopupBehaviour.Modal,
                Placement = PopupPlacement.ScreenCenter(),
                CloseTriggers = PopupCloseTriggers.Escape | PopupCloseTriggers.ActionButton
            };
            foreach (var b in vm.Buttons)
                config.Buttons.Add(new PopupButton(b.Text, b.Id, b.IsPrimary));

            var result = await _popups!.Open(config).Result;

            // Esc / dismiss with no button maps to the last button, matching DynamicPopup.
            var buttonId = result.ButtonId
                ?? (vm.Buttons.Count > 0 ? vm.Buttons[^1].Id : string.Empty);
            return new DialogResult(buttonId, vm.HasInput ? result.InputText : null);
        }

        private sealed class PopupModalHandle : IDisposable
        {
            private readonly IPopupHandle _handle;
            private bool _disposed;

            public PopupModalHandle(IPopupHandle handle) => _handle = handle;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _handle.Close();
            }
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
