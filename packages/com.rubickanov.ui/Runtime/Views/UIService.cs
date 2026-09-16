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
        private readonly VisualElement _screenLayer;
        private readonly VisualElement _hudLayer;
        private readonly VisualElement _popupLayer;
        private readonly VisualElement _overlayLayer;
        private readonly Dictionary<Type, View> _views = new();
        private readonly Dictionary<Type, UILayer> _viewLayers = new();
        private readonly Dictionary<Type, object> _loading = new();
        private View? _activeScreen;
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
            _screenLayer = RequireLayer(root, "screen-layer");
            _hudLayer = RequireLayer(root, "hud-layer");
            _popupLayer = RequireLayer(root, "popup-layer");
            _overlayLayer = RequireLayer(root, "overlay-layer");
#if UNITY_EDITOR
            DebugRegistry.Register(this);
#endif
        }

#if UNITY_EDITOR
        public IReadOnlyDictionary<Type, View> DebugViews => _views;
        public IReadOnlyDictionary<Type, UILayer> DebugViewLayers => _viewLayers;
        public View? DebugActiveScreen => _activeScreen;
        public IReadOnlyList<View> DebugPopupStack => _popupStack;
        public int DebugPointerCaptureCount => _captureCount;
        public int DebugBackStackDepth => _backHandlers.Count;
#endif

        public ReadOnlyReactiveProperty<bool> PointerCaptured => _pointerCaptured;

        public async UniTask Register<T>() where T : View
        {
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
            GetLayerContainer(layer).Add(view.Root);
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

        private VisualElement GetLayerContainer(UILayer layer) => layer switch
        {
            UILayer.Screen => _screenLayer,
            UILayer.HUD => _hudLayer,
            UILayer.Popup => _popupLayer,
            UILayer.Overlay => _overlayLayer,
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null)
        };

        private static VisualElement RequireLayer(VisualElement root, string name)
        {
            var element = root.Q(name);
            if (element == null)
                throw new InvalidOperationException(
                    $"UI root is missing required child '{name}'. " +
                    "Expected elements: screen-layer, hud-layer, popup-layer, overlay-layer.");
            return element;
        }

        public void Unregister<T>() where T : View
        {
            var type = typeof(T);
            if (_loading.Remove(type)) return;
            if (!_views.TryGetValue(type, out var view)) return;

            _views.Remove(type);
            _viewLayers.Remove(type);
            RemoveFromStacks(view);
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
            if (!view.ViewModelType.IsInstanceOfType(viewModel))
                throw new ArgumentException(
                    $"View {typeof(T).Name} expects a view model of type {view.ViewModelType.Name}, " +
                    $"got {viewModel.GetType().Name}.", nameof(viewModel));

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

            switch (_viewLayers[typeof(T)])
            {
                case UILayer.Screen:
                    var previous = _activeScreen;
                    _activeScreen = view;
                    if (previous != null && previous != view)
                    {
                        ReleaseInput(previous);
                        previous.HideAsync().Forget();
                    }
                    AcquireInput(view);
                    break;
                case UILayer.Popup:
                    _popupStack.Remove(view);
                    _popupStack.Add(view);
                    AcquireInput(view);
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

        /// <summary>Clears the popup stack (top first) and the active screen, returning what they held.</summary>
        private List<View> TakeScreenAndPopups()
        {
            var views = new List<View>(_popupStack.Count + 1);
            for (int i = _popupStack.Count - 1; i >= 0; i--) views.Add(_popupStack[i]);
            if (_activeScreen != null) views.Add(_activeScreen);

            _popupStack.Clear();
            _activeScreen = null;
            foreach (var view in views) ReleaseInput(view);
            return views;
        }

        private void RemoveFromStacks(View view)
        {
            if (_activeScreen == view) _activeScreen = null;
            _popupStack.Remove(view);
            ReleaseInput(view);
        }

        // ── Input: pointer capture and back stack (D8, D9) ───────

        /// <summary>Takes a fresh capture and back handler for a visible screen or popup, on top of the others.</summary>
        private void AcquireInput(View view)
        {
            ReleaseInput(view);
            view.PointerCapture = CapturePointer();
            view.BackHandle = PushBackHandler(view.HandleBack);
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
            if (_disposed) throw new ObjectDisposedException(nameof(UIService));

            var entry = new BackHandler(this, handler);
            _backHandlers.Add(entry);
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
    public static class DebugRegistry
    {
        public static readonly List<UIService> Instances = new();
        public static void Register(UIService service) => Instances.Add(service);
        public static void Unregister(UIService service) => Instances.Remove(service);
    }
#endif
}
