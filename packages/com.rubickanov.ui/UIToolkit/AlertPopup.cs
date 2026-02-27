using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public class AlertPopup : UIToolkitView<AlertViewModel>
    {
        protected override UniTask OnBind()
        {
            Root.Q<Label>("title-label").text = ViewModel.Title;
            Root.Q<Label>("message-label").text = ViewModel.Message;

            var okBtn = Root.Q<Button>("ok-btn");

            if (string.IsNullOrEmpty(ViewModel.ButtonText))
            {
                okBtn.style.display = DisplayStyle.None;
            }
            else
            {
                okBtn.text = ViewModel.ButtonText;
                BindButton(okBtn, () => ViewModel.CompletionSource.TrySetResult());
            }

            Root.RegisterCallback<NavigationCancelEvent>(OnNavigationCancel);
            TrackUnbind(() => Root.UnregisterCallback<NavigationCancelEvent>(OnNavigationCancel));

            Root.focusable = true;
            Root.Focus();

            return UniTask.CompletedTask;
        }

        private void OnNavigationCancel(NavigationCancelEvent evt)
        {
            evt.StopPropagation();
            ViewModel.CompletionSource.TrySetResult();
        }
    }
}
