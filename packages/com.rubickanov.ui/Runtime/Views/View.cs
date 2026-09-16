using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    public abstract class View
    {
        public VisualElement Root { get; internal set; } = default!;
        public bool IsVisible { get; private set; }

        /// <summary>
        /// Name of the UXML asset the view is built from. <c>null</c> means no UXML: the view builds its tree in
        /// <see cref="OnInitialize"/> on an empty root.
        /// </summary>
        protected virtual string? UxmlName => GetType().Name;

        internal string? ResolveUxmlName() => UxmlName;
        internal UIService? Service { get; set; }

        internal void Initialize() => OnInitialize();

        protected virtual bool InterceptsInput => false;

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

        public async UniTask ShowAsync()
        {
            if (IsVisible) return;
            IsVisible = true;
            NoneAnimation.Instance.Reset(Root);
            Root.style.display = DisplayStyle.Flex;
            if (InterceptsInput)
                Root.pickingMode = PickingMode.Position;
            await OnShowAsync();
        }

        public async UniTask HideAsync()
        {
            if (!IsVisible) return;
            await OnHideAsync();
            IsVisible = false;
            OnHide();
            Root.style.display = DisplayStyle.None;
            Root.pickingMode = PickingMode.Ignore;
        }

        public void Destroy()
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                ForceUnbind();
            }
            Root.RemoveFromHierarchy();
        }

        internal virtual void ForceUnbind() { }

        protected abstract UniTask OnBind(ViewModelBase viewModel);
        protected virtual void OnInitialize() { }
        protected virtual void OnHide() { }

        protected virtual UniTask OnShowAsync() => UniTask.CompletedTask;
        protected virtual UniTask OnHideAsync() => UniTask.CompletedTask;
    }
}
