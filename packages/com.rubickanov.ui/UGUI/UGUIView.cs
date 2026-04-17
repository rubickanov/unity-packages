using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Rubickanov.UI.UGUI
{
    public abstract class UGUIView<TViewModel> : UGUIViewBase where TViewModel : ViewModelBase
    {
        protected TViewModel ViewModel { get; private set; } = default!;

        private DisposableBag _disposables;
        private readonly List<Action> _unbindActions = new();
        private readonly List<IView> _children = new();

        protected sealed override async UniTask OnBind(ViewModelBase viewModel)
        {
            ViewModel = (TViewModel)viewModel;
            await OnBind();
        }

        protected sealed override void OnHide()
        {
            OnViewHide();
            Unbind();
        }

        private void Unbind()
        {
            OnUnbind();
            UnbindAll();
            DestroyChildren();
            ViewModel = default!;
        }

        internal override void ForceUnbind()
        {
            if (ViewModel is null) return;
            Unbind();
        }

        private void UnbindAll()
        {
            _disposables.Dispose();
            _disposables = new DisposableBag();
            foreach (var unbind in _unbindActions) unbind();
            _unbindActions.Clear();
        }

        protected abstract UniTask OnBind();
        protected virtual void OnViewHide() { }
        protected virtual void OnUnbind() { }

        public T GetService<T>() where T : class
            => ServiceResolver is null
                ? throw new InvalidOperationException("IViewServiceResolver is not set on this view.")
                : ServiceResolver.Require<T>();

        public void BindObservable<T>(Observable<T> observable, Action<T> handler)
            => Bind(observable, handler);

        // ── Observable binding ───────────────────────────────────────

        protected void Bind<T>(Observable<T> observable, Action<T> handler)
        {
            observable.Subscribe(handler).AddTo(ref _disposables);
        }

        // ── Button ───────────────────────────────────────────────────

        protected void BindButton(Button button, Action handler)
        {
            button.onClick.AddListener(handler.Invoke);
            _unbindActions.Add(() => button.onClick.RemoveListener(handler.Invoke));
        }

        // ── TMP_InputField ↔ ReactiveProperty<string> ───────────────

        protected void BindInputField(TMP_InputField field, ReactiveProperty<string> property)
        {
            field.text = property.Value;
            Bind(property, v => { if (field.text != v) field.text = v; });

            void OnChanged(string v) => property.Value = v;
            field.onValueChanged.AddListener(OnChanged);
            _unbindActions.Add(() => field.onValueChanged.RemoveListener(OnChanged));
        }

        // ── Slider ↔ ReactiveProperty<float> ────────────────────────

        protected void BindSlider(Slider slider, ReactiveProperty<float> property)
        {
            slider.value = property.Value;
            Bind(property, v => { if (Math.Abs(slider.value - v) > float.Epsilon) slider.value = v; });

            void OnChanged(float v) => property.Value = v;
            slider.onValueChanged.AddListener(OnChanged);
            _unbindActions.Add(() => slider.onValueChanged.RemoveListener(OnChanged));
        }

        // ── Toggle ↔ ReactiveProperty<bool> ─────────────────────────

        protected void BindToggle(Toggle toggle, ReactiveProperty<bool> property)
        {
            toggle.isOn = property.Value;
            Bind(property, v => { if (toggle.isOn != v) toggle.isOn = v; });

            void OnChanged(bool v) => property.Value = v;
            toggle.onValueChanged.AddListener(OnChanged);
            _unbindActions.Add(() => toggle.onValueChanged.RemoveListener(OnChanged));
        }

        // ── TMP_Dropdown ↔ ReactiveProperty<int> ────────────────────

        protected void BindDropdown(TMP_Dropdown dropdown, ReactiveProperty<int> property,
            List<string> choices)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(choices);
            dropdown.value = property.Value;
            Bind(property, v => { if (dropdown.value != v) dropdown.value = v; });

            void OnChanged(int v) => property.Value = v;
            dropdown.onValueChanged.AddListener(OnChanged);
            _unbindActions.Add(() => dropdown.onValueChanged.RemoveListener(OnChanged));
        }

        // ── One-way: element → callback (with initial value) ─────────

        protected void BindSlider(Slider slider, float initialValue, Action<float> onChange)
        {
            slider.value = initialValue;
            slider.onValueChanged.AddListener(onChange.Invoke);
            _unbindActions.Add(() => slider.onValueChanged.RemoveListener(onChange.Invoke));
        }

        protected void BindToggle(Toggle toggle, bool initialValue, Action<bool> onChange)
        {
            toggle.isOn = initialValue;
            toggle.onValueChanged.AddListener(onChange.Invoke);
            _unbindActions.Add(() => toggle.onValueChanged.RemoveListener(onChange.Invoke));
        }

        protected void BindDropdown(TMP_Dropdown dropdown, List<string> choices, int initialIndex,
            Action<int> onChange)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(choices);
            dropdown.value = initialIndex;
            dropdown.onValueChanged.AddListener(onChange.Invoke);
            _unbindActions.Add(() => dropdown.onValueChanged.RemoveListener(onChange.Invoke));
        }

        // ── Manual cleanup tracking ─────────────────────────────────

        protected void TrackUnbind(Action action) => _unbindActions.Add(action);

        // ── Child views ──────────────────────────────────────────────

        protected async UniTask<TView> CreateChild<TView, TVM>(TVM viewModel, Transform? container = null)
            where TView : UGUIView<TVM>
            where TVM : ViewModelBase
        {
            if (ViewFactory == null)
                throw new InvalidOperationException("ViewFactory is not set. Cannot create child views.");

            var childView = await ViewFactory.Create<TView>(UILayer.HUD);

            if (childView is UGUIViewBase uguiChild)
            {
                ViewFactory.Detach(childView);
                if (container != null)
                    uguiChild.transform.SetParent(container, false);
            }

            await childView.Bind(viewModel);
            childView.Show();
            _children.Add(childView);
            return (TView)childView;
        }

        protected void DestroyChildren()
        {
            foreach (var child in _children) child.Destroy();
            _children.Clear();
        }
    }
}
