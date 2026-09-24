using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    /// <summary>Dialogs: modal popups centered on the overlay layer that close on <see cref="IUIService.Back"/>.</summary>
    public static class DialogExtensions
    {
        /// <summary>Starts a dialog; add a message, content, input and buttons, then open it.</summary>
        public static PopupBuilder CreateDialog(this IPopupService popups, string title) =>
            popups.Create()
                .Title(title)
                .Modal()
                .At(PopupPlacement.ScreenCenter())
                .Class(PopupStyle.Dialog)
                .CloseOn(PopupCloseTriggers.Escape);

        /// <summary>True when the confirm button closed the dialog; false for the cancel button or Back.</summary>
        public static async UniTask<bool> ShowConfirm(this IPopupService popups, string title, string message,
            string confirmText = "Yes", string cancelText = "No")
        {
            var result = await popups.CreateDialog(title)
                .Message(message)
                .Button(confirmText, "confirm", isPrimary: true)
                .Button(cancelText, "cancel")
                .OpenAsync();
            return result.ButtonId == "confirm";
        }

        public static async UniTask ShowAlert(this IPopupService popups, string title, string message,
            string buttonText = "OK")
        {
            await popups.CreateDialog(title)
                .Message(message)
                .Button(buttonText, "ok", isPrimary: true)
                .OpenAsync();
        }

        /// <summary>A modal message with no way to close it but the handle: dispose it when the wait is over.</summary>
        public static IPopupHandle ShowModal(this IPopupService popups, string title, string message) =>
            popups.Create()
                .Title(title)
                .Message(message)
                .Modal()
                .Open();
    }
}
