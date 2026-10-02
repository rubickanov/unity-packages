using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    public class UIService : IUIService, IDetachedViews, IDisposable
    {
        private readonly UxmlLoader _loadUxml;
        private readonly UILayerElements _layers;
        private readonly Dictionary<Type, View> _views = new();
        private readonly Dictionary<Type, UILayer> _viewLayers = new();
        // Popup views: an instance never shown, holding the UXML every popup's own instance is built from.
        private readonly Dictionary<Type, View> _popupViews = new();
        private readonly Dictionary<Type, object> _loading = new();
        private View? _activeScreen;
        private readonly List<HistoryEntry> _history = new();
        private readonly ReactiveProperty<bool> _pointerCaptured = new(false);
        private int _captureCount;
        private readonly List<BackHandler> _backHandlers = new();
        private bool _navigateBackPending;
        private bool _disposed;

        /// <param name="root">
        /// Element holding the layer children: screen-layer, hud-layer, popup-layer, overlay-layer. The popup layer
        /// belongs to <see cref="PopupHost"/>.
        /// </param>
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
        internal IReadOnlyCollection<Type> DebugPopupViews => _popupViews.Keys;
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
            if (_views.ContainsKey(type) || _popupViews.ContainsKey(type))
                throw new InvalidOperationException($"View {type.Name} is already registered in UIService.");
            if (_loading.ContainsKey(type))
                throw new InvalidOperationException($"View {type.Name} is already being registered in UIService.");

            var token = new object();
            _loading[type] = token;

            var view = Activator.CreateInstance<T>();
            UxmlCache cache;
            try
            {
                cache = await LoadUxml(view.ResolveUxmlName(), type);
            }
            catch
            {
                if (_loading.TryGetValue(type, out var current) && current == token)
                    _loading.Remove(type);
                throw;
            }

            if (!_loading.TryGetValue(type, out var owner) || owner != token)
            {
                cache.Release();
                throw new OperationCanceledException(
                    $"Registration of view {type.Name} was cancelled by Unregister while its UXML was loading.");
            }
            _loading.Remove(type);

            var layer = view.ResolveLayer();
            if (layer == UILayer.Popup)
            {
                view.Uxml = cache;
                view.OwnsUxml = true;
                view.Owner = this;
                _popupViews[type] = view;
                return;
            }

            try
            {
                view.Root = cache.Asset != null ? cache.Asset.CloneTree() : new VisualElement();
                view.Uxml = cache;
                view.OwnsUxml = true;
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

            _layers[layer].Add(view.Root);
            _views[type] = view;
            _viewLayers[type] = layer;
        }

        private async UniTask<UxmlCache> LoadUxml(string? uxmlName, Type viewType)
        {
            if (uxmlName == null) return new UxmlCache(null, null);

            var (asset, handle) = await _loadUxml(uxmlName);
            if (asset == null)
            {
                handle?.Dispose();
                throw new InvalidOperationException($"Failed to load UXML '{uxmlName}' for view {viewType.Name}.");
            }
            return new UxmlCache(asset, handle);
        }

        public void Unregister<T>() where T : View
        {
            var type = typeof(T);
            if (_loading.Remove(type)) return;
            if (_popupViews.Remove(type, out var popupView))
            {
                // Popups still showing it keep the UXML until they close.
                popupView.ReleaseUxml();
                return;
            }
            if (!_views.TryGetValue(type, out var view)) return;

            _views.Remove(type);
            _viewLayers.Remove(type);
            RemoveFromScreen(view);
            _history.RemoveAll(entry => entry.ViewType == type);
            view.Destroy();
        }

        /// <exception cref="InvalidOperationException">
        /// <typeparamref name="T"/> is not registered, or is a popup view, which has no instance of its own.
        /// </exception>
        public T Get<T>() where T : View
        {
            if (_views.TryGetValue(typeof(T), out var view))
                return (T)view;
            if (_popupViews.ContainsKey(typeof(T)))
                throw new InvalidOperationException(
                    $"View {typeof(T).Name} is a popup view: every popup builds its own instance. " +
                    "Show it with IPopupService.ShowView.");
            throw new InvalidOperationException($"View {typeof(T).Name} is not registered in UIService.");
        }

        public async UniTask Show<TView, TViewModel>(TViewModel viewModel)
            where TView : View<TViewModel>
            where TViewModel : ViewModelBase
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));

            await ShowView(Get<TView>(), viewModel, _history.Clear);
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
                RemoveFromScreen(view);
                throw;
            }

            if (_viewLayers[view.GetType()] == UILayer.Screen)
            {
                var previous = _activeScreen;
                _activeScreen = view;
                if (previous != null && previous != view)
                {
                    ReleaseInput(previous);
                    previous.HideAsync().Forget();
                }
                AcquireInput(view, bottom: true);
                onScreenShown();
            }
            else if (view.ResolveInterceptsInput() && view.BackHandle == null)
            {
                AcquireInput(view, bottom: false);
            }

            try
            {
                await animation;
            }
            catch
            {
                if (view.State == ViewState.Showing)
                {
                    RemoveFromScreen(view);
                    view.Hide();
                }
                throw;
            }
        }

        // ── Screen history ───────────────────────────────────────

        public bool CanNavigateBack => _history.Count > 1;

        public async UniTask Navigate<TView, TViewModel>(Func<TViewModel> createViewModel)
            where TView : View<TViewModel>
            where TViewModel : ViewModelBase
        {
            if (createViewModel == null) throw new ArgumentNullException(nameof(createViewModel));

            var type = typeof(TView);
            var view = Get<TView>();
            if (_viewLayers[type] != UILayer.Screen)
                throw new InvalidOperationException(
                    $"View {type.Name} is on the {_viewLayers[type]} layer: only screens are navigated to.");

            Func<ViewModelBase> create = createViewModel;
            var viewModel = CreateViewModel(view, create);
            await ShowView(view, viewModel, () =>
            {
                var existing = _history.FindIndex(entry => entry.ViewType == type);
                if (existing >= 0) _history.RemoveRange(existing, _history.Count - existing);
                _history.Add(new HistoryEntry(type, create));
            });
        }

        /// <summary>
        /// Shows the previous screen of the history with a new view model from its factory. Returns false, changing
        /// nothing, when there is none.
        /// </summary>
        internal async UniTask<bool> NavigateBack()
        {
            if (!CanNavigateBack) return false;

            var target = _history[^2];
            var view = _views[target.ViewType];
            var viewModel = CreateViewModel(view, target.CreateViewModel);
            await ShowView(view, viewModel, () => _history.RemoveAt(_history.Count - 1));
            return true;
        }

        /// <summary>
        /// The default <c>OnBack</c> of a screen: when the screen is the history's current one, goes back once the back
        /// handler has returned, so the screen's override still has its view model after <c>base.OnBack()</c>.
        /// </summary>
        internal bool NavigateBackFrom(View screen)
        {
            if (!CanNavigateBack || _activeScreen != screen) return false;

            _navigateBackPending = true;
            return true;
        }

        private void RunPendingNavigation()
        {
            if (!_navigateBackPending) return;
            _navigateBackPending = false;
            NavigateBack().Forget();
        }

        private static ViewModelBase CreateViewModel(View view, Func<ViewModelBase> createViewModel) =>
            createViewModel()
            ?? throw new InvalidOperationException($"The view model factory of {view.GetType().Name} returned null.");

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
        /// layer and the screen, bound to <paramref name="viewModel"/> and shown. The caller adds its root and destroys
        /// it; until then it holds the UXML, even through <see cref="Unregister{T}"/>.
        /// </summary>
        View IDetachedViews.CreateDetached(Type viewType, ViewModelBase viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            if (_disposed) throw new ObjectDisposedException(nameof(UIService));
            if (!_views.TryGetValue(viewType, out var registered) && !_popupViews.TryGetValue(viewType, out registered))
                throw new InvalidOperationException(
                    $"View {viewType.Name} is not registered in UIService: register it to load its UXML before " +
                    "showing it in a popup.");

            var uxml = registered.Uxml!;
            uxml.Retain();
            var view = (View)Activator.CreateInstance(viewType);
            view.Uxml = uxml;
            view.OwnsUxml = true;
            try
            {
                view.InitializeFrom(uxml, $"view {viewType.Name} is being unregistered");
                view.Owner = this;
                view.ShowDetached(viewModel);
            }
            catch
            {
                if (view.Root != null) view.Destroy();
                else view.ReleaseUxml();
                throw;
            }
            return view;
        }

        public void Hide<T>() where T : View
        {
            if (!_views.TryGetValue(typeof(T), out var view)) return;

            RemoveFromScreen(view);
            view.Hide();
        }

        public UniTask HideAsync<T>() where T : View
        {
            if (!_views.TryGetValue(typeof(T), out var view)) return UniTask.CompletedTask;

            RemoveFromScreen(view);
            return view.HideAsync();
        }

        private void RemoveFromScreen(View view)
        {
            if (_activeScreen == view)
            {
                _activeScreen = null;
                _history.Clear();
            }
            ReleaseInput(view);
        }

        // ── Input: pointer capture and back stack ────────────────

        /// <summary>
        /// Takes a fresh capture and back handler for a view that takes input. The visible screen's handler goes under
        /// all the others (<paramref name="bottom"/>), so whatever is open over the screen answers Back first, even when
        /// it opened before the screen was shown.
        /// </summary>
        private void AcquireInput(View view, bool bottom)
        {
            ReleaseInput(view);
            view.PointerCapture = CapturePointer();
            view.BackHandle = AddBackHandler(view.HandleBack, bottom);
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
            try
            {
                for (int i = snapshot.Length - 1; i >= 0; i--)
                {
                    var entry = snapshot[i];
                    if (entry.Removed) continue;
                    if (entry.Handler()) return true;
                }
                return false;
            }
            finally
            {
                RunPendingNavigation();
            }
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
            _navigateBackPending = false;
            _activeScreen = null;
            _history.Clear();
            foreach (var view in _views.Values) ReleaseInput(view);

            List<Exception>? errors = null;
            foreach (var view in _views.Values)
            {
                try { view.Destroy(); }
                catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
            }
            foreach (var template in _popupViews.Values)
            {
                try { template.ReleaseUxml(); }
                catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
            }

            _views.Clear();
            _viewLayers.Clear();
            _popupViews.Clear();

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
