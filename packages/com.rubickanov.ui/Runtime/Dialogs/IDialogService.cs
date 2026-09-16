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
    }
}
