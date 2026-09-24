using System;
using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public interface IDialogService
    {
        UniTask<bool> ShowConfirm(string title, string message,
            string confirmText = "Yes", string cancelText = "No");

        UniTask ShowAlert(string title, string message, string buttonText = "OK");

        IDisposable ShowModal(string title, string message);

        /// <summary>Starts a custom dialog: a modal popup on the overlay layer that closes on Back.</summary>
        DialogBuilder CreateDialog(string title);
    }
}
