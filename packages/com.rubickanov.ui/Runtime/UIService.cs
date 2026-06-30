using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public class UIService : IUIService, IDisposable
    {
        private readonly IViewFactory _factory;
        private Action<bool>? _onUIVisibilityChanged;
        private readonly Dictionary<Type, IView> _views = new();
        private readonly Dictionary<Type, UILayer> _viewLayers = new();
        private IView? _activeScreen;
        private readonly List<IView> _popupStack = new();

        public UIService(IViewFactory factory)
        {
            _factory = factory;
#if UNITY_EDITOR
            DebugRegistry.Register(this);
#endif
        }

#if UNITY_EDITOR
        public IReadOnlyDictionary<Type, IView> DebugViews => _views;
        public IReadOnlyDictionary<Type, UILayer> DebugViewLayers => _viewLayers;
        public IView? DebugActiveScreen => _activeScreen;
        public IReadOnlyList<IView> DebugPopupStack => _popupStack;
#endif

        public void SetVisibilityCallback(Action<bool> callback) => _onUIVisibilityChanged = callback;

        public async UniTask Register<T>(UILayer layer) where T : class, IView
        {
            var view = await _factory.Create<T>(layer);
            var type = typeof(T);
            _views[type] = view;
            _viewLayers[type] = layer;
        }

        public void Unregister<T>() where T : IView
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
            _factory.Detach(view);
            _views.Remove(type);
            _viewLayers.Remove(type);
        }

        public T Get<T>() where T : IView
        {
            if (!_views.TryGetValue(typeof(T), out var view))
                throw new InvalidOperationException(
                    $"View {typeof(T).Name} is not registered in UIService.");
            return (T)view;
        }

        public async UniTask Show<T>(ViewModelBase viewModel) where T : IView
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

        public void Hide<T>() where T : IView
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

        public async UniTask HideAsync<T>(float duration = 0.3f) where T : IView
        {
            if (!_views.TryGetValue(typeof(T), out var view))
            {
                return;
            }

            if (_activeScreen == view)
            {
                await _activeScreen.HideAsync(duration);
                _activeScreen = null;
            }
            else
            {
                await view.HideAsync(duration);
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

        public async UniTask HideTopAsync(float duration = 0.3f)
        {
            if (_popupStack.Count == 0)
            {
                return;
            }

            var top = _popupStack[^1];
            await top.HideAsync(duration);
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

        public async UniTask HideAllAsync(float duration = 0.3f)
        {
            if (_activeScreen == null && _popupStack.Count == 0) return;

            var tasks = new List<UniTask>(_popupStack.Count + 1);
            if (_activeScreen != null) tasks.Add(_activeScreen.HideAsync(duration));
            foreach (var popup in _popupStack) tasks.Add(popup.HideAsync(duration));

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
                _factory.Detach(view);
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
