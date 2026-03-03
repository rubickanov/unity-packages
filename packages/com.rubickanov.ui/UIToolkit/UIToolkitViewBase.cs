using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public abstract class UIToolkitViewBase : IView
    {
        public VisualElement Root { get; internal set; } = default!;
        public bool IsVisible { get; private set; }

        internal abstract string? UxmlName { get; }
        internal IViewFactory? ViewFactory { get; set; }

        internal void Initialize() => OnInitialize();

        protected virtual bool InterceptsInput => true;

        private IAnimationTarget? _animationTarget;
        private IAnimationTarget AnimationTarget => _animationTarget ??= new UIToolkitAnimationTarget(Root);

        public async UniTask Bind(ViewModelBase viewModel)
        {
            await OnBind(viewModel);
        }

        public void Show()
        {
            IsVisible = true;
            Root.style.display = DisplayStyle.Flex;
            if (InterceptsInput)
                Root.pickingMode = PickingMode.Position;
        }

        public void Hide()
        {
            if (!IsVisible) return;
            IsVisible = false;
            OnHide();
            Root.style.display = DisplayStyle.None;
            Root.pickingMode = PickingMode.Ignore;
        }

        public async UniTask ShowAsync(float duration = 0.3f)
        {
            if (IsVisible) return;
            IsVisible = true;
            AnimationTarget.ResetAnimationState();
            Root.style.display = DisplayStyle.Flex;
            if (InterceptsInput)
                Root.pickingMode = PickingMode.Position;
            await OnShowAsync(AnimationTarget, duration);
        }

        public async UniTask HideAsync(float duration = 0.3f)
        {
            if (!IsVisible) return;
            await OnHideAsync(AnimationTarget, duration);
            IsVisible = false;
            OnHide();
            Root.style.display = DisplayStyle.None;
            Root.pickingMode = PickingMode.Ignore;
        }

        public void Destroy()
        {
            Hide();
            Root.RemoveFromHierarchy();
        }

        protected abstract UniTask OnBind(ViewModelBase viewModel);
        protected virtual void OnInitialize() { }
        protected virtual void OnHide() { }

        protected virtual UniTask OnShowAsync(IAnimationTarget root, float duration)
            => UniTask.CompletedTask;

        protected virtual UniTask OnHideAsync(IAnimationTarget root, float duration)
            => UniTask.CompletedTask;
    }
}
