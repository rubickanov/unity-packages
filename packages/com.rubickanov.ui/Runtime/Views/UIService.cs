using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    public class UIService : IUIService, IDisposable
    {
        private readonly UxmlLoader _loadUxml;
        private readonly UILayerElements _layers;
        private readonly Dictionary<Type, View> _views = new();
        private readonly Dictionary<Type, UILayer> _viewLayers = new();
        private readonly Dictionary<Type, object> _loading = new();
        private View? _activeScreen;
        private readonly List<HistoryEntry> _history = new();
        private readonly List<View> _popupStack = new();
        private readonly ReactiveProperty<bool> _pointerCaptured = new(false);
        private int _captureCount;
        private readonly List<BackHandler> _backHandlers = new();
        private bool _disposed;

        /// <param name="root">Element holding the layer children: screen-layer, hud-layer, popup-layer, overlay-layer.</param>
        /// <param name="loader">Loads the UXML asset of a view by name.</param>
        public UIService(VisualElement root, UxmlLoader loader)
        {
            _loadUxml = loader;
            _layers = new UILayerElements(root);
#if UNITY_EDITOR
            DebugRegistry.Register(this);
#endif
        }

#if UNITY_EDITOR
        internal IReadOnlyDictionary<Type, View> DebugViews => _views;
        internal IReadOnlyDictionary<Type, UILayer> DebugViewLayers => _viewLayers;
        internal View? DebugActiveScreen => _activeScreen;
        internal IReadOnlyList<View> DebugPopupStack => _popupStack;
        internal int DebugPointerCaptureCount => _captureCount;
        internal int DebugBackStackDepth => _backHandlers.Count;

        internal IReadOnlyList<Type> DebugScreenHistory
        {
            get
            {
                var types = new List<Type>(_history.Count);
                foreach (var entry in _history) types.Add(entry.ViewType);
                return types;
            }
        }
#endif

        public ReadOnlyReactiveProperty<bool> PointerCaptured => _pointerCaptured;

        public async UniTask Register<T>() where T : View
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIService));

            var type = typeof(T);
            if (_views.ContainsKey(type))
                throw new InvalidOperationException($"View {type.Name} is already registered in UIService.");
            if (_loading.ContainsKey(type))
                throw new InvalidOperationException($"View {type.Name} is already being registered in UIService.");

            var token = new object();
            _loading[type] = token;

            var view = Activator.CreateInstance<T>();
            var cache = new UxmlCache();
            try
            {
                await LoadUxml(cache, view, type);
            }
            catch
            {
                if (_loading.TryGetValue(type, out var current) && current == token)
                    _loading.Remove(type);
                cache.Release();
                throw;
            }

            if (!_loading.TryGetValue(type, out var owner) || owner != token)
            {
                cache.Release();
                throw new OperationCanceledException(
                    $"Registration of view {type.Name} was cancelled by Unregister while its UXML was loading.");
            }
            _loading.Remove(type);

            try
            {
                cache.TryGet(type, out var asset);
                view.Root = asset != null ? asset.CloneTree() : new VisualElement();
                view.Uxml = cache;
                view.Root.pickingMode = PickingMode.Ignore;
                view.Root.style.display = DisplayStyle.None;
                view.Root.style.position = Position.Absolute;
                view.Root.style.left = view.Root.style.top =
                    view.Root.style.right = view.Root.style.bottom = 0;
                view.Owner = this;
                view.Initialize();
            }
            catch
            {
                cache.Release();
                throw;
            }

            var layer = view.ResolveLayer();
            _layers[layer].Add(view.Root);
            _views[type] = view;
            _viewLayers[type] = layer;
        }

        private async UniTask LoadUxml(UxmlCache cache, View view, Type viewType)
        {
            var uxmlName = view.ResolveUxmlName();
            VisualTreeAsset? asset = null;
            if (uxmlName != null)
            {
                var (loaded, handle) = await _loadUxml(uxmlName);
                cache.AddHandle(handle);
                if (loaded == null)
                    throw new InvalidOperationException(
                        $"Failed to load UXML '{uxmlName}' for view {viewType.Name}.");
                asset = loaded;
            }
            cache.Add(viewType, asset);

            var children = view.ResolveChildViews();
            for (int i = 0; i < children.Count; i++)
            {
                var childType = children[i];
                if (cache.Contains(childType)) continue;
                if (childType == null || childType.IsAbstract || !typeof(View).IsAssignableFrom(childType))
                    throw new InvalidOperationException(
                        $"View {viewType.Name} lists '{childType?.Name}' in ChildViews, which is not a concrete view type.");

                var child = (View)Activator.CreateInstance(childType);
                await LoadUxml(cache, child, childType);
            }
        }

        public void Unregister<T>() where T : View
        {
            var type = typeof(T);
            if (_loading.Remove(type)) return;
            if (!_views.TryGetValue(type, out var view)) return;

            _views.Remove(type);
            _viewLayers.Remove(type);
            RemoveFromStacks(view);
            _history.RemoveAll(entry => entry.ViewType == type);
            DestroyView(view);
        }

        private static void DestroyView(View view)
        {
            try
            {
                view.Destroy();
            }
            finally
            {
                view.Uxml?.Release();
                view.Uxml = null;
            }
        }

        public T Get<T>() where T : View
        {
            if (!_views.TryGetValue(typeof(T), out var view))
                throw new InvalidOperationException(
                    $"View {typeof(T).Name} is not registered in UIService.");
            return (T)view;
        }

        public async UniTask Show<T>(ViewModelBase viewModel) where T : View
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));

            var view = Get<T>();
            EnsureViewModelType(view, viewModel);
            await ShowView(view, viewModel, _history.Clear);
        }

        /// <param name="onScreenShown">
        /// For a screen, updates the history once the view is bound and the screen is active, before the animation.
        /// </param>
        private async UniTask ShowView(View view, ViewModelBase viewModel, Action onScreenShown)
        {
            UniTask animation;
            try
            {
                animation = view.BeginShow(viewModel);
            }
            catch
            {
                RemoveFromStacks(view);
                throw;
            }

            switch (_viewLayers[view.GetType()])
            {
                case UILayer.Screen:
                    var previous = _activeScreen;
                    _activeScreen = view;
                    if (previous != null && previous != view)
                    {
                        ReleaseInput(previous);
                        previous.HideAsync().Forget();
                    }
                    AcquireInput(view, screen: true);
                    onScreenShown();
                    break;
                case UILayer.Popup:
                    _popupStack.Remove(view);
                    _popupStack.Add(view);
                    AcquireInput(view, screen: false);
                    break;
            }

            try
            {
                await animation;
            }
            catch
            {
                if (view.State == ViewState.Showing)
                {
                    RemoveFromStacks(view);
                    view.Hide();
                }
                throw;
            }
        }

        private static void EnsureViewModelType(View view, ViewModelBase viewModel)
        {
            if (!view.ViewModelType.IsInstanceOfType(viewModel))
                throw new ArgumentException(
                    $"View {view.GetType().Name} expects a view model of type {view.ViewModelType.Name}, " +
                    $"got {viewModel.GetType().Name}.", nameof(viewModel));
        }

        // ── Screen history ───────────────────────────────────────

        public bool CanNavigateBack => _history.Count > 1;

        public async UniTask Navigate<T>(Func<ViewModelBase> createViewModel) where T : View
        {
            if (createViewModel == null) throw new ArgumentNullException(nameof(createViewModel));

            var type = typeof(T);
            var view = Get<T>();
            if (_viewLayers[type] != UILayer.Screen)
                throw new InvalidOperationException(
                    $"View {type.Name} is on the {_viewLayers[type]} layer: only screens are navigated to.");

            var viewModel = CreateViewModel(view, createViewModel);
            await ShowView(view, viewModel, () =>
            {
                var existing = _history.FindIndex(entry => entry.ViewType == type);
                if (existing >= 0) _history.RemoveRange(existing, _history.Count - existing);
                _history.Add(new HistoryEntry(type, createViewModel));
            });
        }

        public async UniTask<bool> NavigateBack()
        {
            if (!CanNavigateBack) return false;

            var target = _history[^2];
            var view = _views[target.ViewType];
            var viewModel = CreateViewModel(view, target.CreateViewModel);
            await ShowView(view, viewModel, () => _history.RemoveAt(_history.Count - 1));
            return true;
        }

        /// <summary>The default <c>OnBack</c> of a screen: goes back when the screen is the history's current one.</summary>
        internal bool NavigateBackFrom(View screen)
        {
            if (!CanNavigateBack || _activeScreen != screen) return false;

            NavigateBack().Forget();
            return true;
        }

        private static ViewModelBase CreateViewModel(View view, Func<ViewModelBase> createViewModel)
        {
            var viewModel = createViewModel()
                ?? throw new InvalidOperationException($"The view model factory of {view.GetType().Name} returned null.");
            try
            {
                EnsureViewModelType(view, viewModel);
            }
            catch
            {
                viewModel.Dispose();
                throw;
            }
            return viewModel;
        }

        private readonly struct HistoryEntry
        {
            public readonly Type ViewType;
            public readonly Func<ViewModelBase> CreateViewModel;

            public HistoryEntry(Type viewType, Func<ViewModelBase> createViewModel)
            {
                ViewType = viewType;
                CreateViewModel = createViewModel;
            }
        }

        // ── Views as popup content ───────────────────────────────

        /// <summary>
        /// Creates a new instance of the registered view <paramref name="viewType"/> from its UXML, apart from its
        /// layer and stacks, bound to <paramref name="viewModel"/> and shown. The caller adds its root and destroys it.
        /// </summary>
        internal View CreateDetached(Type viewType, ViewModelBase viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            if (!_views.TryGetValue(viewType, out var registered))
                throw new InvalidOperationException(
                    $"View {viewType.Name} is not registered in UIService: register it to load its UXML before " +
                    "showing it as popup content.");
            EnsureViewModelType(registered, viewModel);

            var view = (View)Activator.CreateInstance(viewType);
            view.InitializeFrom(registered.Uxml, $"view {viewType.Name} is being unregistered");
            try
            {
                view.ShowAsChild(viewModel);
            }
            catch
            {
                view.Destroy();
                throw;
            }
            return view;
        }

        public void Hide<T>() where T : View
        {
            if (!_views.TryGetValue(typeof(T), out var view)) return;

            RemoveFromStacks(view);
            view.Hide();
        }

        public UniTask HideAsync<T>() where T : View
        {
            if (!_views.TryGetValue(typeof(T), out var view)) return UniTask.CompletedTask;

            return HideViewAsync(view);
        }

        internal UniTask HideViewAsync(View view)
        {
            RemoveFromStacks(view);
            return view.HideAsync();
        }

        public void HideTop()
        {
            if (_popupStack.Count == 0) return;

            var top = _popupStack[^1];
            RemoveFromStacks(top);
            top.Hide();
        }

        public UniTask HideTopAsync()
        {
            if (_popupStack.Count == 0) return UniTask.CompletedTask;

            return HideViewAsync(_popupStack[^1]);
        }

        public void HideAll()
        {
            var views = TakeScreenAndPopups();
            foreach (var view in views) view.Hide();
        }

        public UniTask HideAllAsync()
        {
            var views = TakeScreenAndPopups();
            if (views.Count == 0) return UniTask.CompletedTask;

            var tasks = new UniTask[views.Count];
            for (int i = 0; i < views.Count; i++) tasks[i] = views[i].HideAsync();
            return UniTask.WhenAll(tasks);
        }

        /// <summary>Clears the popup stack (top first), the active screen and the history, returning what they held.</summary>
        private List<View> TakeScreenAndPopups()
        {
            var views = new List<View>(_popupStack.Count + 1);
            for (int i = _popupStack.Count - 1; i >= 0; i--) views.Add(_popupStack[i]);
            if (_activeScreen != null) views.Add(_activeScreen);

            _popupStack.Clear();
            _activeScreen = null;
            _history.Clear();
            foreach (var view in views) ReleaseInput(view);
            return views;
        }

        private void RemoveFromStacks(View view)
        {
            if (_activeScreen == view)
            {
                _activeScreen = null;
                _history.Clear();
            }
            _popupStack.Remove(view);
            ReleaseInput(view);
        }

        // ── Input: pointer capture and back stack (D8, D9) ───────

        /// <summary>
        /// Takes a fresh capture and back handler for a visible screen or popup. A popup's handler goes on top of the
        /// others; a screen's goes under all of them, so whatever is open over the screen answers Back first, even
        /// when it opened before the screen was shown.
        /// </summary>
        private void AcquireInput(View view, bool screen)
        {
            ReleaseInput(view);
            view.PointerCapture = CapturePointer();
            view.BackHandle = AddBackHandler(view.HandleBack, bottom: screen);
        }

        private static void ReleaseInput(View view)
        {
            var capture = view.PointerCapture;
            var back = view.BackHandle;
            view.PointerCapture = null;
            view.BackHandle = null;
            capture?.Dispose();
            back?.Dispose();
        }

        public IDisposable CapturePointer()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIService));

            _captureCount++;
            _pointerCaptured.Value = true;
            return new PointerCaptureHandle(this);
        }

        private void ReleasePointer()
        {
            if (_disposed || _captureCount == 0) return;

            _captureCount--;
            _pointerCaptured.Value = _captureCount > 0;
        }

        public IDisposable PushBackHandler(Func<bool> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return AddBackHandler(handler, bottom: false);
        }

        private BackHandler AddBackHandler(Func<bool> handler, bool bottom)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIService));

            var entry = new BackHandler(this, handler);
            if (bottom) _backHandlers.Insert(0, entry);
            else _backHandlers.Add(entry);
            return entry;
        }

        public bool Back()
        {
            if (_backHandlers.Count == 0) return false;

            // Snapshot: a handler usually removes itself (a popup hides) or pushes another.
            var snapshot = _backHandlers.ToArray();
            for (int i = snapshot.Length - 1; i >= 0; i--)
            {
                var entry = snapshot[i];
                if (entry.Removed) continue;
                if (entry.Handler()) return true;
            }
            return false;
        }

        private sealed class PointerCaptureHandle : IDisposable
        {
            private UIService? _owner;

            public PointerCaptureHandle(UIService owner) => _owner = owner;

            public void Dispose()
            {
                var owner = _owner;
                _owner = null;
                owner?.ReleasePointer();
            }
        }

        private sealed class BackHandler : IDisposable
        {
            private readonly UIService _owner;
            public readonly Func<bool> Handler;
            public bool Removed { get; private set; }

            public BackHandler(UIService owner, Func<bool> handler)
            {
                _owner = owner;
                Handler = handler;
            }

            public void Dispose()
            {
                if (Removed) return;
                Removed = true;
                _owner._backHandlers.Remove(this);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _loading.Clear();
            _popupStack.Clear();
            _activeScreen = null;
            _history.Clear();
            foreach (var view in _views.Values) ReleaseInput(view);

            List<Exception>? errors = null;
            foreach (var view in _views.Values)
            {
                try { DestroyView(view); }
                catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
            }

            _views.Clear();
            _viewLayers.Clear();

            foreach (var entry in _backHandlers.ToArray()) entry.Dispose();
            _captureCount = 0;
            _pointerCaptured.Value = false;
            _disposed = true;
            _pointerCaptured.Dispose();
#if UNITY_EDITOR
            DebugRegistry.Unregister(this);
#endif
            if (errors != null)
                throw new AggregateException(errors);
        }
    }

#if UNITY_EDITOR
    internal static class DebugRegistry
    {
        public static readonly List<UIService> Instances = new();
        public static void Register(UIService service) => Instances.Add(service);
        public static void Unregister(UIService service) => Instances.Remove(service);
    }
#endif
}
