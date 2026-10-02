using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using R3;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    public abstract class View<TViewModel> : View where TViewModel : ViewModelBase
    {
        private TViewModel? _viewModel;
        private DisposableBag _disposables;
        private readonly List<Action> _unbindActions = new();

        protected TViewModel ViewModel => _viewModel!;

        internal sealed override Type ViewModelType => typeof(TViewModel);
        internal sealed override ViewModelBase? BoundViewModel => _viewModel;

        internal sealed override void BindViewModel(ViewModelBase viewModel)
        {
            _viewModel = (TViewModel)viewModel;
            OnBind();
        }

        /// <summary>
        /// Clears bindings and disposes the bound view model. Every step runs even when an earlier
        /// one throws, so the view always ends unbound; the failures are rethrown at the end.
        /// </summary>
        internal sealed override void Unbind()
        {
            var viewModel = _viewModel;
            if (viewModel is null) return;

            List<Exception>? errors = null;
            try { OnUnbind(); }
            catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }

            UnbindAll(ref errors);

            _viewModel = null;
            try { viewModel.Dispose(); }
            catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }

            if (errors == null) return;
            if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException(errors);
        }

        private void UnbindAll(ref List<Exception>? errors)
        {
            var disposables = _disposables;
            _disposables = new DisposableBag();
            try { disposables.Dispose(); }
            catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }

            foreach (var unbind in _unbindActions)
            {
                try { unbind(); }
                catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
            }
            _unbindActions.Clear();
        }

        /// <summary>Called when a view model is bound. Everything bound here is cleared on unbind.</summary>
        protected abstract void OnBind();
        protected virtual void OnUnbind() { }

        protected void Bind<T>(Observable<T> observable, Action<T> handler)
        {
            observable.Subscribe(handler).AddTo(ref _disposables);
        }

        protected void BindText<T>(Label label, Observable<T> observable, Func<T, string> format)
        {
            Bind(observable, value => label.text = format(value));
        }

        protected void BindVisible(VisualElement element, Observable<bool> visible)
        {
            Bind(visible, value => element.style.display = value ? DisplayStyle.Flex : DisplayStyle.None);
        }

        protected void BindClass(VisualElement element, string className, Observable<bool> enabled)
        {
            Bind(enabled, value => element.EnableInClassList(className, value));
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

        // Bind applies the property's current value at once: a ReactiveProperty emits it on subscribe. Setting a
        // field's value to the one it holds sends no ChangeEvent, and the property does not re-emit an equal value,
        // so the round trip ends by itself.

        protected void BindTextField(TextField field, ReactiveProperty<string> property)
        {
            // TextField rewrites its displayed text even for an equal value, which would drop an IME composition in
            // progress: set it only when the value differs.
            Bind(property, v => { if (field.value != v) field.value = v; });
            BindValueChanged<TextField, string>(field, v => property.Value = v);
        }

        protected void BindSlider(Slider slider, ReactiveProperty<float> property)
        {
            Bind(property, v => slider.value = v);
            BindValueChanged<Slider, float>(slider, v => property.Value = v);
        }

        protected void BindToggle(Toggle toggle, ReactiveProperty<bool> property)
        {
            Bind(property, v => toggle.value = v);
            BindValueChanged<Toggle, bool>(toggle, v => property.Value = v);
        }

        protected void BindDropdown(DropdownField dropdown, ReactiveProperty<int> property,
            List<string> choices)
        {
            dropdown.choices = choices;
            Bind(property, v => dropdown.index = v);
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
    }
}
