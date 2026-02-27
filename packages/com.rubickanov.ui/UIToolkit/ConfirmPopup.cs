using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public class ConfirmPopup : UIToolkitView<ConfirmViewModel>
    {
        protected override UniTask OnBind()
        {
            Root.Q<Label>("title-label").text = ViewModel.Title;
            Root.Q<Label>("message-label").text = ViewModel.Message;

            var confirmBtn = Root.Q<Button>("confirm-btn");
            var cancelBtn = Root.Q<Button>("cancel-btn");

            confirmBtn.text = ViewModel.ConfirmText;
            cancelBtn.text = ViewModel.CancelText;

            BindButton(confirmBtn, () => ViewModel.CompletionSource.TrySetResult(true));
            BindButton(cancelBtn, () => ViewModel.CompletionSource.TrySetResult(false));

            Root.RegisterCallback<NavigationCancelEvent>(OnNavigationCancel);
            TrackUnbind(() => Root.UnregisterCallback<NavigationCancelEvent>(OnNavigationCancel));

            Root.focusable = true;
            Root.Focus();

            return UniTask.CompletedTask;
        }

        private void OnNavigationCancel(NavigationCancelEvent evt)
        {
            evt.StopPropagation();
            ViewModel.CompletionSource.TrySetResult(false);
        }
    }
}
