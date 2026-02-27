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
        private IView? _activeScreen;
        private readonly List<IView> _popupStack = new();

        public UIService(IViewFactory factory)
        {
            _factory = factory;
        }

        public void SetVisibilityCallback(Action<bool> callback) => _onUIVisibilityChanged = callback;

        public async UniTask Register<T>(UILayer layer) where T : class, IView
        {
            var view = await _factory.Create<T>(layer);
            _views[typeof(T)] = view;
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
            _views.Remove(type);
        }

        public T Get<T>() where T : IView => (T)_views[typeof(T)];

        public async UniTask ShowScreen<T>(ViewModelBase viewModel) where T : IView
        {
            _activeScreen?.Hide();
            var view = Get<T>();
            await view.Bind(viewModel);
            await view.ShowAsync();
            _activeScreen = view;
            _onUIVisibilityChanged?.Invoke(true);
        }

        public void HideScreen<T>() where T : IView
        {
            if (!_views.TryGetValue(typeof(T), out var view) || _activeScreen != view)
            {
                return;
            }

            _activeScreen.Hide();
            _activeScreen = null;

            if (_popupStack.Count == 0)
            {
                _onUIVisibilityChanged?.Invoke(false);
            }
        }

        public async UniTask HideScreenAsync<T>(float duration = 0.3f) where T : IView
        {
            if (!_views.TryGetValue(typeof(T), out var view) || _activeScreen != view)
            {
                return;
            }

            await _activeScreen.HideAsync(duration);
            _activeScreen = null;

            if (_popupStack.Count == 0)
            {
                _onUIVisibilityChanged?.Invoke(false);
            }
        }

        public void HideAllScreens()
        {
            _activeScreen?.Hide();
            _activeScreen = null;

            if (_popupStack.Count == 0)
            {
                _onUIVisibilityChanged?.Invoke(false);
            }
        }

        public async UniTask ShowPopup<T>(ViewModelBase viewModel) where T : IView
        {
            var popup = Get<T>();
            await popup.Bind(viewModel);
            await popup.ShowAsync();
            _popupStack.Add(popup);
            _onUIVisibilityChanged?.Invoke(true);
        }

        public void HidePopup<T>() where T : IView
        {
            var popup = Get<T>();
            popup.Hide();
            _popupStack.Remove(popup);

            if (_popupStack.Count == 0 && _activeScreen == null)
            {
                _onUIVisibilityChanged?.Invoke(false);
            }
        }

        public async UniTask HidePopupAsync<T>(float duration = 0.3f) where T : IView
        {
            var popup = Get<T>();
            await popup.HideAsync(duration);
            _popupStack.Remove(popup);

            if (_popupStack.Count == 0 && _activeScreen == null)
            {
                _onUIVisibilityChanged?.Invoke(false);
            }
        }

        public void HideTopPopup()
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

        public async UniTask HideTopPopupAsync(float duration = 0.3f)
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

        public void Dispose()
        {
            foreach (var view in _views.Values)
            {
                view.Destroy();
            }

            _views.Clear();
            _popupStack.Clear();
            _activeScreen = null;
        }
    }
}
