using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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
        private Action<bool>? _onUIVisibilityChanged;
        private readonly Dictionary<Type, View> _views = new();
        private readonly Dictionary<Type, UILayer> _viewLayers = new();
        private readonly Dictionary<View, IDisposable> _uxmlHandles = new();
        private View? _activeScreen;
        private readonly List<View> _popupStack = new();

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
#endif

        public void SetVisibilityCallback(Action<bool> callback) => _onUIVisibilityChanged = callback;

        public async UniTask Register<T>(UILayer layer) where T : View
        {
            var view = await CreateView<T>();
            view.Root.style.position = Position.Absolute;
            view.Root.style.left = view.Root.style.top =
                view.Root.style.right = view.Root.style.bottom = 0;
            view.Initialize();
            GetLayerContainer(layer).Add(view.Root);

            var type = typeof(T);
            _views[type] = view;
            _viewLayers[type] = layer;
        }

        internal async UniTask<T> CreateChildView<T>() where T : View
        {
            var view = await CreateView<T>();
            view.Initialize();
            ReleaseUxml(view);
            return view;
        }

        private async UniTask<T> CreateView<T>() where T : View
        {
            var view = Activator.CreateInstance<T>();
            var uxmlName = view.ResolveUxmlName();

            if (uxmlName != null)
            {
                var (asset, handle) = await _loadUxml(uxmlName);
                if (asset == null)
                    throw new InvalidOperationException(
                        $"Failed to load UXML '{uxmlName}' for view {typeof(T).Name}.");
                _uxmlHandles[view] = handle;
                view.Root = asset.CloneTree();
            }
            else
            {
                view.Root = new VisualElement();
            }

            view.Root.pickingMode = PickingMode.Ignore;
            view.Root.style.display = DisplayStyle.None;
            view.Service = this;
            return view;
        }

        private void ReleaseUxml(View view)
        {
            if (_uxmlHandles.Remove(view, out var handle))
                handle.Dispose();
        }

        private VisualElement GetLayerContainer(UILayer layer) => layer switch
        {
            UILayer.Screen => _screenLayer,
            UILayer.HUD => _hudLayer,
            UILayer.Popup => _popupLayer,
            UILayer.Overlay => _overlayLayer,
            _ => _screenLayer
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
            if (!_views.TryGetValue(type, out var view))
            {
                return;
            }

            if (_activeScreen == view)
            {
                _activeScreen.Hide();
                _activeScreen = null;
            }

            _popupStack.Remove(view);
            view.Destroy();
            ReleaseUxml(view);
            _views.Remove(type);
            _viewLayers.Remove(type);
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
            var type = typeof(T);
            var view = Get<T>();
            var layer = _viewLayers[type];

            if (layer == UILayer.Screen)
            {
                _activeScreen?.Hide();
                _activeScreen = view;
                try
                {
                    await view.Bind(viewModel);
                    await view.ShowAsync();
                }
                catch
                {
                    view.Hide();
                    _activeScreen = null;
                    throw;
                }
            }
            else
            {
                if (_popupStack.Contains(view))
                {
                    view.Hide();
                    _popupStack.Remove(view);
                }

                _popupStack.Add(view);
                try
                {
                    await view.Bind(viewModel);
                    await view.ShowAsync();
                }
                catch
                {
                    view.Hide();
                    _popupStack.Remove(view);
                    throw;
                }
            }

            _onUIVisibilityChanged?.Invoke(true);
        }

        public void Hide<T>() where T : View
        {
            if (!_views.TryGetValue(typeof(T), out var view))
            {
                return;
            }

            if (_activeScreen == view)
            {
                _activeScreen.Hide();
                _activeScreen = null;
            }
            else
            {
                view.Hide();
                _popupStack.Remove(view);
            }

            if (_popupStack.Count == 0 && _activeScreen == null)
            {
                _onUIVisibilityChanged?.Invoke(false);
            }
        }

        public async UniTask HideAsync<T>() where T : View
        {
            if (!_views.TryGetValue(typeof(T), out var view))
            {
                return;
            }

            if (_activeScreen == view)
            {
                await _activeScreen.HideAsync();
                _activeScreen = null;
            }
            else
            {
                await view.HideAsync();
                _popupStack.Remove(view);
            }

            if (_popupStack.Count == 0 && _activeScreen == null)
            {
                _onUIVisibilityChanged?.Invoke(false);
            }
        }

        public void HideTop()
        {
            if (_popupStack.Count == 0)
            {
                return;
            }

            var top = _popupStack[^1];
            top.Hide();
            _popupStack.RemoveAt(_popupStack.Count - 1);

            if (_popupStack.Count == 0 && _activeScreen == null)
            {
                _onUIVisibilityChanged?.Invoke(false);
            }
        }

        public async UniTask HideTopAsync()
        {
            if (_popupStack.Count == 0)
            {
                return;
            }

            var top = _popupStack[^1];
            await top.HideAsync();
            _popupStack.RemoveAt(_popupStack.Count - 1);

            if (_popupStack.Count == 0 && _activeScreen == null)
            {
                _onUIVisibilityChanged?.Invoke(false);
            }
        }

        public void HideAll()
        {
            if (_activeScreen == null && _popupStack.Count == 0) return;

            _activeScreen?.Hide();
            _activeScreen = null;

            foreach (var popup in _popupStack)
            {
                popup.Hide();
            }

            _popupStack.Clear();
            _onUIVisibilityChanged?.Invoke(false);
        }

        public async UniTask HideAllAsync()
        {
            if (_activeScreen == null && _popupStack.Count == 0) return;

            var tasks = new List<UniTask>(_popupStack.Count + 1);
            if (_activeScreen != null) tasks.Add(_activeScreen.HideAsync());
            foreach (var popup in _popupStack) tasks.Add(popup.HideAsync());

            await UniTask.WhenAll(tasks);

            _activeScreen = null;
            _popupStack.Clear();
            _onUIVisibilityChanged?.Invoke(false);
        }

        public void Dispose()
        {
            foreach (var view in _views.Values)
            {
                view.Destroy();
                ReleaseUxml(view);
            }

            _views.Clear();
            _viewLayers.Clear();
            _popupStack.Clear();
            _activeScreen = null;
#if UNITY_EDITOR
            DebugRegistry.Unregister(this);
#endif
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
