using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    public abstract class View<TViewModel> : View where TViewModel : ViewModelBase
    {
        protected TViewModel ViewModel { get; private set; } = default!;

        private DisposableBag _disposables;
        private readonly List<Action> _unbindActions = new();
        private readonly List<View> _children = new();

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

        public void Bind<T>(Observable<T> observable, Action<T> handler)
        {
            observable.Subscribe(handler).AddTo(ref _disposables);
        }

        protected void BindButton(Button button, Action handler)
        {
            button.clicked += handler;
            _unbindActions.Add(() => button.clicked -= handler);
        }

        protected void TrackUnbind(Action action) => _unbindActions.Add(action);

        protected void BindValueChanged<TElement, TValue>(TElement element, Action<TValue> handler)
            where TElement : INotifyValueChanged<TValue>
        {
            EventCallback<ChangeEvent<TValue>> cb = e => handler(e.newValue);
            element.RegisterValueChangedCallback(cb);
            _unbindActions.Add(() => element.UnregisterValueChangedCallback(cb));
        }

        // ── Two-way: element ↔ ReactiveProperty ─────────────────

        protected void BindTextField(TextField field, ReactiveProperty<string> property)
        {
            field.value = property.Value;
            Bind(property, v => { if (field.value != v) field.value = v; });
            BindValueChanged<TextField, string>(field, v => property.Value = v);
        }

        protected void BindSlider(Slider slider, ReactiveProperty<float> property)
        {
            slider.value = property.Value;
            Bind(property, v => { if (Math.Abs(slider.value - v) > float.Epsilon) slider.value = v; });
            BindValueChanged<Slider, float>(slider, v => property.Value = v);
        }

        protected void BindToggle(Toggle toggle, ReactiveProperty<bool> property)
        {
            toggle.value = property.Value;
            Bind(property, v => { if (toggle.value != v) toggle.value = v; });
            BindValueChanged<Toggle, bool>(toggle, v => property.Value = v);
        }

        protected void BindDropdown(DropdownField dropdown, ReactiveProperty<int> property,
            List<string> choices)
        {
            dropdown.choices = choices;
            dropdown.index = property.Value;
            Bind(property, v => { if (dropdown.index != v) dropdown.index = v; });
            BindValueChanged<DropdownField, string>(dropdown, _ => property.Value = dropdown.index);
        }

        // ── One-way: element → callback (with initial value) ─────

        protected void BindSlider(Slider slider, float initialValue, Action<float> onChange)
        {
            slider.value = initialValue;
            BindValueChanged<Slider, float>(slider, onChange);
        }

        protected void BindToggle(Toggle toggle, bool initialValue, Action<bool> onChange)
        {
            toggle.value = initialValue;
            BindValueChanged<Toggle, bool>(toggle, onChange);
        }

        protected void BindDropdown(DropdownField dropdown, List<string> choices, int initialIndex,
            Action<int> onChange)
        {
            dropdown.choices = choices;
            dropdown.index = initialIndex;
            BindValueChanged<DropdownField, string>(dropdown, _ => onChange(dropdown.index));
        }

        protected async UniTask<TView> CreateChild<TView, TVM>(TVM viewModel, VisualElement? container = null)
            where TView : View<TVM>, new()
            where TVM : ViewModelBase
        {
            if (Service == null)
                throw new InvalidOperationException("View is not registered in a UIService. Cannot create child views.");

            var childView = await Service.CreateChildView<TView>();
            container?.Add(childView.Root);

            await childView.Bind(viewModel);
            childView.Show();
            _children.Add(childView);
            return childView;
        }

        protected void DestroyChildren()
        {
            foreach (var child in _children) child.Destroy();
            _children.Clear();
        }
    }
}
