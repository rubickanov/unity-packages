using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Rubickanov.UI.UGUI
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UGUIViewBase : MonoBehaviour, IView
    {
        public bool IsVisible { get; private set; }

        internal IViewFactory? ViewFactory { get; set; }
        internal IViewServiceResolver? ServiceResolver { get; set; }
        internal virtual string PrefabName => GetType().Name;

        protected virtual bool InterceptsInput => true;

        private CanvasGroup _canvasGroup = default!;
        private RectTransform _rectTransform = default!;
        private bool _destroyed;

        private IAnimationTarget? _animationTarget;
        private IAnimationTarget AnimationTarget =>
            _animationTarget ??= new UGUIAnimationTarget(_canvasGroup, _rectTransform);

        internal void Initialize()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _rectTransform = GetComponent<RectTransform>();
            OnInitialize();
        }

        async UniTask IView.Bind(ViewModelBase viewModel)
        {
            await OnBind(viewModel);
        }

        public void Show()
        {
            IsVisible = true;
            gameObject.SetActive(true);
            ConfigureCanvasGroup(true);
        }

        public void Hide()
        {
            if (!IsVisible) return;
            IsVisible = false;
            OnHide();
            ConfigureCanvasGroup(false);
            gameObject.SetActive(false);
        }

        public async UniTask ShowAsync(float duration = 0.3f)
        {
            if (IsVisible) return;
            IsVisible = true;
            AnimationTarget.ResetAnimationState();
            gameObject.SetActive(true);
            ConfigureCanvasGroup(true);
            await OnShowAsync(AnimationTarget, duration);
        }

        public async UniTask HideAsync(float duration = 0.3f)
        {
            if (!IsVisible) return;
            await OnHideAsync(AnimationTarget, duration);
            IsVisible = false;
            OnHide();
            ConfigureCanvasGroup(false);
            gameObject.SetActive(false);
        }

        public void Destroy()
        {
            if (_destroyed) return;
            _destroyed = true;
            Hide();
            Object.Destroy(gameObject);
        }

        private void ConfigureCanvasGroup(bool visible)
        {
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.blocksRaycasts = visible && InterceptsInput;
            _canvasGroup.interactable = visible && InterceptsInput;
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
