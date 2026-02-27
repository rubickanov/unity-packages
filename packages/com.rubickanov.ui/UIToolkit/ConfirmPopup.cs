using Cysharp.Threading.Tasks;
using Rubickanov.UI.Animations;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public class ConfirmPopup : UIToolkitView<ConfirmViewModel>
    {
        private UIToolkitAnimationTarget _overlay = default!;
        private UIToolkitAnimationTarget _panel = default!;

        protected override void OnInitialize()
        {
            _overlay = new UIToolkitAnimationTarget(Root.Q(className: "overlay"));
            _panel = new UIToolkitAnimationTarget(Root.Q(className: "panel"));
        }

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

        protected override async UniTask OnShowAsync(IAnimationTarget root, float duration)
        {
            await UniTask.WhenAll(
                ViewAnimations.Fade.PlayShowAsync(_overlay, duration),
                ViewAnimations.FadeAndScale.PlayShowAsync(_panel, duration));
        }

        protected override async UniTask OnHideAsync(IAnimationTarget root, float duration)
        {
            await UniTask.WhenAll(
                ViewAnimations.Fade.PlayHideAsync(_overlay, duration),
                ViewAnimations.FadeAndScale.PlayHideAsync(_panel, duration));
        }

        private void OnNavigationCancel(NavigationCancelEvent evt)
        {
            evt.StopPropagation();
            ViewModel.CompletionSource.TrySetResult(false);
        }
    }
}
