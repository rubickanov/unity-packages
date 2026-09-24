using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using ObservableCollections;
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
            if (container == null) throw new ArgumentNullException(nameof(container));

            return AddChild<TView>(viewModel, container, container.childCount);
        }

        /// <summary>Creates, inserts at <paramref name="index"/> of <paramref name="container"/>, binds and shows a child.</summary>
        private TView AddChild<TView>(ViewModelBase viewModel, VisualElement container, int index)
            where TView : View, new()
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));

            var childType = typeof(TView);
            if (!IsListedChild(childType))
                throw new InvalidOperationException(
                    $"View {GetType().Name} cannot create child {childType.Name}: it is not listed in ChildViews.");

            var child = new TView();
            child.InitializeFrom(Uxml, $"view {GetType().Name} must be registered in a UIService");
            container.Insert(index, child.Root);

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

        /// <summary>Destroys one child, unless <see cref="DestroyChildren"/> already has.</summary>
        private void DestroyChild(View child)
        {
            if (_children.Remove(child))
                child.Destroy();
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

        // ── Lists ────────────────────────────────────────────────

        /// <summary>
        /// Keeps one child view per item of <paramref name="source"/> in <paramref name="container"/>, in the
        /// source's order: an added item gets a row with a view model from <paramref name="createViewModel"/>, a
        /// removed one loses its row and the row's view model, a moved or sorted one keeps its row. Rows stay
        /// together, after whatever else the container holds (a header, an "empty" label). Change the source on the
        /// main thread.
        /// </summary>
        /// <exception cref="InvalidOperationException"><typeparamref name="TView"/> is not listed in <c>ChildViews</c>.</exception>
        protected void BindList<TItem, TView, TVM>(IObservableCollection<TItem> source, VisualElement container,
            Func<TItem, TVM> createViewModel)
            where TView : View<TVM>, new()
            where TVM : ViewModelBase
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (createViewModel == null) throw new ArgumentNullException(nameof(createViewModel));
            if (!IsListedChild(typeof(TView)))
                throw new InvalidOperationException(
                    $"View {GetType().Name} cannot bind a list of {typeof(TView).Name}: it is not listed in ChildViews.");

            var binding = new ListBinding<TItem, TView>(this, source, container, createViewModel);
            TrackUnbind(binding.Dispose);
            binding.Start();
        }

        /// <summary>Rows of one <see cref="BindList{TItem,TView,TVM}"/>, index for index with the source.</summary>
        private sealed class ListBinding<TItem, TView> where TView : View, new()
        {
            private readonly View<TViewModel> _owner;
            private readonly IObservableCollection<TItem> _source;
            private readonly VisualElement _container;
            private readonly Func<TItem, ViewModelBase> _createViewModel;
            private readonly NotifyCollectionChangedEventHandler<TItem> _onChanged;
            private readonly List<Row> _rows = new();
            private readonly EqualityComparer<TItem> _comparer = EqualityComparer<TItem>.Default;

            private readonly struct Row
            {
                public readonly TItem Item;
                public readonly View View;

                public Row(TItem item, View view)
                {
                    Item = item;
                    View = view;
                }
            }

            public ListBinding(View<TViewModel> owner, IObservableCollection<TItem> source, VisualElement container,
                Func<TItem, ViewModelBase> createViewModel)
            {
                _owner = owner;
                _source = source;
                _container = container;
                _createViewModel = createViewModel;
                _onChanged = OnChanged;
            }

            public void Start()
            {
                _source.CollectionChanged += _onChanged;
                Reconcile();
            }

            // The rows themselves go with the owner's children when it unbinds.
            public void Dispose() => _source.CollectionChanged -= _onChanged;

            private void OnChanged(in NotifyCollectionChangedEventArgs<TItem> e)
            {
                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                        if (e.IsSingleItem)
                        {
                            Insert(e.NewStartingIndex, e.NewItem);
                        }
                        else
                        {
                            var items = e.NewItems;
                            for (int i = 0; i < items.Length; i++)
                                Insert(e.NewStartingIndex < 0 ? -1 : e.NewStartingIndex + i, items[i]);
                        }
                        break;

                    case NotifyCollectionChangedAction.Remove:
                        if (e.IsSingleItem)
                        {
                            RemoveAt(Find(e.OldStartingIndex, e.OldItem));
                        }
                        else
                        {
                            var items = e.OldItems;
                            for (int i = 0; i < items.Length; i++)
                                RemoveAt(Find(e.OldStartingIndex, items[i]));
                        }
                        break;

                    case NotifyCollectionChangedAction.Replace:
                        if (e.IsSingleItem)
                        {
                            Replace(Find(e.OldStartingIndex, e.OldItem), e.NewItem);
                        }
                        else
                        {
                            var oldItems = e.OldItems;
                            var newItems = e.NewItems;
                            for (int i = 0; i < newItems.Length; i++)
                                Replace(Find(e.OldStartingIndex < 0 ? -1 : e.OldStartingIndex + i, oldItems[i]),
                                    newItems[i]);
                        }
                        break;

                    case NotifyCollectionChangedAction.Move:
                        Move(Find(e.OldStartingIndex, e.NewItem), e.NewStartingIndex);
                        break;

                    // Clear, sort, reverse, or anything a collection reports without details.
                    default:
                        Reconcile();
                        break;
                }
            }

            /// <summary>The row of <paramref name="item"/>: at <paramref name="index"/> when it is there, else by equality.</summary>
            private int Find(int index, TItem item)
            {
                if (index >= 0 && index < _rows.Count && _comparer.Equals(_rows[index].Item, item))
                    return index;

                for (int i = 0; i < _rows.Count; i++)
                {
                    if (_comparer.Equals(_rows[i].Item, item)) return i;
                }
                return -1;
            }

            /// <summary>Adds a row at <paramref name="index"/>, or at the end when it is negative.</summary>
            private void Insert(int index, TItem item)
            {
                if (index < 0 || index > _rows.Count) index = _rows.Count;

                var view = _owner.AddChild<TView>(_createViewModel(item), _container, ContainerIndex(index));
                _rows.Insert(index, new Row(item, view));
            }

            private void RemoveAt(int index)
            {
                if (index < 0) return;

                var view = _rows[index].View;
                _rows.RemoveAt(index);
                _owner.DestroyChild(view);
            }

            private void Replace(int index, TItem item)
            {
                if (index < 0)
                {
                    Insert(-1, item);
                    return;
                }

                RemoveAt(index);
                Insert(index, item);
            }

            private void Move(int from, int to)
            {
                if (from < 0 || from == to) return;

                var row = _rows[from];
                _rows.RemoveAt(from);
                if (to < 0 || to > _rows.Count) to = _rows.Count;

                row.View.Root.RemoveFromHierarchy();
                _container.Insert(ContainerIndex(to), row.View.Root);
                _rows.Insert(to, row);
            }

            /// <summary>Where the row at <paramref name="index"/> goes among the container's children.</summary>
            private int ContainerIndex(int index)
            {
                if (index < _rows.Count)
                    return IndexInContainer(_rows[index].View);
                if (_rows.Count > 0)
                    return Math.Min(IndexInContainer(_rows[^1].View) + 1, _container.childCount);
                return _container.childCount;
            }

            /// <summary>A row destroyed early by <c>DestroyChildren</c> has left the container: count it at the end.</summary>
            private int IndexInContainer(View row)
            {
                var index = _container.IndexOf(row.Root);
                return index >= 0 ? index : _container.childCount;
            }

            /// <summary>
            /// Makes the rows match the source again, keeping the row (and view model) of every item still in it.
            /// </summary>
            private void Reconcile()
            {
                var anchor = _rows.Count > 0 ? IndexInContainer(_rows[0].View) : _container.childCount;
                var old = new List<Row>(_rows);
                _rows.Clear();

                foreach (var item in _source)
                {
                    var match = -1;
                    for (int i = 0; i < old.Count; i++)
                    {
                        if (!_comparer.Equals(old[i].Item, item)) continue;
                        match = i;
                        break;
                    }

                    if (match >= 0)
                    {
                        _rows.Add(old[match]);
                        old.RemoveAt(match);
                    }
                    else
                    {
                        var view = _owner.AddChild<TView>(_createViewModel(item), _container, _container.childCount);
                        _rows.Add(new Row(item, view));
                    }
                }

                foreach (var row in old)
                    _owner.DestroyChild(row.View);

                for (int i = 0; i < _rows.Count; i++)
                    _rows[i].View.Root.RemoveFromHierarchy();
                anchor = Math.Min(anchor, _container.childCount);
                for (int i = 0; i < _rows.Count; i++)
                    _container.Insert(anchor + i, _rows[i].View.Root);
            }
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
