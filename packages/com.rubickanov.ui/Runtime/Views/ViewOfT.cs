using System;
using System.Collections.Generic;
using R3;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    public abstract class View<TViewModel> : View where TViewModel : ViewModelBase
    {
        private TViewModel? _viewModel;
        private DisposableBag _disposables;
        private readonly List<Action> _unbindActions = new();
        private readonly List<View> _children = new();

        protected TViewModel ViewModel => _viewModel!;

        internal sealed override Type ViewModelType => typeof(TViewModel);
        internal sealed override ViewModelBase? BoundViewModel => _viewModel;

        internal sealed override void BindViewModel(ViewModelBase viewModel)
        {
            _viewModel = (TViewModel)viewModel;
            OnBind();
        }

        /// <summary>Clears bindings, destroys children and disposes the bound view model.</summary>
        internal sealed override void Unbind()
        {
            var viewModel = _viewModel;
            if (viewModel is null) return;

            try
            {
                OnUnbind();
            }
            finally
            {
                UnbindAll();
                DestroyChildren();
                _viewModel = null;
                viewModel.Dispose();
            }
        }

        private void UnbindAll()
        {
            _disposables.Dispose();
            _disposables = new DisposableBag();
            foreach (var unbind in _unbindActions) unbind();
            _unbindActions.Clear();
        }

        /// <summary>Called when a view model is bound. Everything bound here is cleared on unbind.</summary>
        protected abstract void OnBind();
        protected virtual void OnUnbind() { }

        public void Bind<T>(Observable<T> observable, Action<T> handler)
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

        // ── Children ─────────────────────────────────────────────

        /// <summary>
        /// Creates a child view from the UXML loaded at registration, adds it to <paramref name="container"/>, binds
        /// <paramref name="viewModel"/> and shows it. The child is destroyed, and its view model disposed, when this
        /// view unbinds.
        /// </summary>
        /// <exception cref="InvalidOperationException"><typeparamref name="TView"/> is not listed in <c>ChildViews</c>.</exception>
        protected TView CreateChild<TView, TVM>(TVM viewModel, VisualElement container)
            where TView : View<TVM>, new()
            where TVM : ViewModelBase
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            if (container == null) throw new ArgumentNullException(nameof(container));

            var childType = typeof(TView);
            if (!IsListedChild(childType))
                throw new InvalidOperationException(
                    $"View {GetType().Name} cannot create child {childType.Name}: it is not listed in ChildViews.");

            var child = new TView();
            var uxmlName = child.ResolveUxmlName();
            if (uxmlName == null)
            {
                child.Root = new VisualElement();
            }
            else if (Uxml != null && Uxml.TryGet(childType, out var asset) && asset != null)
            {
                child.Root = asset.CloneTree();
            }
            else
            {
                throw new InvalidOperationException(
                    $"UXML '{uxmlName}' of child {childType.Name} is not loaded: view {GetType().Name} must be registered in a UIService.");
            }

            child.Uxml = Uxml;
            child.Initialize();
            container.Add(child.Root);

            try
            {
                child.ShowAsChild(viewModel);
            }
            catch
            {
                child.Destroy();
                throw;
            }

            _children.Add(child);
            return child;
        }

        protected void DestroyChildren()
        {
            List<Exception>? errors = null;
            foreach (var child in _children)
            {
                try { child.Destroy(); }
                catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
            }
            _children.Clear();

            if (errors != null)
                throw new AggregateException(errors);
        }

        private bool IsListedChild(Type childType)
        {
            var listed = ChildViews;
            for (int i = 0; i < listed.Count; i++)
            {
                if (listed[i] == childType) return true;
            }
            return false;
        }
    }
}
