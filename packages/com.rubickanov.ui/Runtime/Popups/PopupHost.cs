using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// Default <see cref="IPopupService"/>. Owns popup elements directly on the UI layers,
    /// supports many open at once, and drives world/cursor followers from one MonoBehaviour-free loop
    /// (the <see cref="SpinnerHost"/> pattern).
    /// </summary>
    public sealed class PopupHost : IPopupService, IPopupHostCallbacks, IDisposable
    {
        private readonly VisualElement _root;
        private readonly VisualElement _screenLayer;
        private readonly VisualElement _hudLayer;
        private readonly VisualElement _popupLayer;
        private readonly VisualElement _overlayLayer;

        private readonly List<PopupInstance> _open = new();
        private readonly List<PopupInstance> _followers = new();

        private readonly IUIService _ui;
        private readonly Func<Vector2>? _pointerScreenPosition;
        private readonly Func<Camera?>? _cameraProvider;
        private readonly IViewAnimation _defaultAnimation;

        private Camera? _worldCamera;
        private int _worldCameraFrame = -1;

        private CancellationTokenSource? _tickCts;
        private Vector2 _cursorPanelPosition;
        private bool _disposed;

        /// <param name="root">Element holding the standard layer elements (for example <c>uiDocument.rootVisualElement</c>).</param>
        /// <param name="ui">
        /// Receives a pointer capture for every open modal or interactive popup and a back handler for every popup
        /// closing on <see cref="PopupCloseTriggers.Escape"/>.
        /// </param>
        /// <param name="defaultStyleSheet">Optional stylesheet applied to every popup.</param>
        /// <param name="pointerScreenPosition">
        /// Live screen-pixel pointer position (bottom-left origin, as from <c>Pointer.current</c> /
        /// <c>Mouse.current</c>), polled every frame for cursor-following popups and converted to
        /// panel space internally. Strongly recommended: UIToolkit runtime panels only dispatch
        /// <see cref="PointerMoveEvent"/> while a pickable element sits under the cursor, so the
        /// event-based fallback freezes over empty areas and a follow popup would stick.
        /// </param>
        /// <param name="camera">Camera for world placements without their own camera. Default: <c>Camera.main</c>.</param>
        /// <param name="animation">Played by popups whose config has no animation. Default: none.</param>
        public PopupHost(VisualElement root, IUIService ui, StyleSheet? defaultStyleSheet = null,
            Func<Vector2>? pointerScreenPosition = null, Func<Camera?>? camera = null, IViewAnimation? animation = null)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _cameraProvider = camera;
            _defaultAnimation = animation ?? NoneAnimation.Instance;
            _screenLayer = RequireLayer("screen-layer");
            _hudLayer = RequireLayer("hud-layer");
            _popupLayer = RequireLayer("popup-layer");
            _overlayLayer = RequireLayer("overlay-layer");
            _pointerScreenPosition = pointerScreenPosition;

            if (defaultStyleSheet != null)
                _root.styleSheets.Add(defaultStyleSheet);

            // Event-based fallback only when no live provider is supplied.
            if (_pointerScreenPosition == null)
                _root.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
        }

        // ── IPopupService ────────────────────────────────────────

        public IPopupHandle Open(PopupConfig config)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PopupHost));

            if (config.DismissOthers)
                CloseAll();

            var layer = GetLayerContainer(config.Layer);
            var instance = new PopupInstance(config, layer, this);
            _open.Add(instance);

            if (instance.IsFollower)
            {
                _followers.Add(instance);
                EnsureTick();
            }

            return instance;
        }

        public PopupBuilder Create() => new(this);

        public void CloseAll()
        {
            // Copy: Close mutates _open via the callback.
            var snapshot = _open.ToArray();
            foreach (var popup in snapshot)
                popup.Close(null, PopupCloseReason.Code);
        }

        private void CloseAllImmediate()
        {
            var snapshot = _open.ToArray();
            foreach (var popup in snapshot)
                popup.CloseImmediate();
        }

        // ── IPopupHostCallbacks (explicit: PopupInstance is internal) ─

        Vector2 IPopupHostCallbacks.CursorPanelPosition =>
            _pointerScreenPosition != null
                ? PopupPlacementResolver.ScreenToPanel(_root, _pointerScreenPosition())
                : _cursorPanelPosition;

        Camera? IPopupHostCallbacks.WorldCamera
        {
            get
            {
                var frame = Time.frameCount;
                if (_worldCameraFrame != frame)
                {
                    var camera = _cameraProvider?.Invoke();
                    _worldCamera = camera != null ? camera : Camera.main;
                    _worldCameraFrame = frame;
                }
                return _worldCamera;
            }
        }

        IUIService IPopupHostCallbacks.Ui => _ui;

        View IPopupHostCallbacks.CreateContentView(Type viewType, ViewModelBase viewModel)
        {
            if (_ui is not UIService service)
                throw new NotSupportedException(
                    $"A view as popup content needs PopupHost built over UIService, which loads its UXML; " +
                    $"this one was given {_ui.GetType().Name}.");
            return service.CreateDetached(viewType, viewModel);
        }
        IViewAnimation IPopupHostCallbacks.DefaultAnimation => _defaultAnimation;

        void IPopupHostCallbacks.OnPopupClosed(PopupInstance instance)
        {
            _open.Remove(instance);
            _followers.Remove(instance);
            if (_followers.Count == 0)
                StopTick();
        }

        void IPopupHostCallbacks.OnPlacementChanged(PopupInstance instance)
        {
            var shouldFollow = instance.IsFollower;
            var isFollowing = _followers.Contains(instance);

            if (shouldFollow && !isFollowing)
            {
                _followers.Add(instance);
                EnsureTick();
            }
            else if (!shouldFollow && isFollowing)
            {
                _followers.Remove(instance);
                if (_followers.Count == 0)
                    StopTick();
            }
        }

        // ── Follower loop (SpinnerHost pattern) ──────────────────

        private void EnsureTick()
        {
            if (_tickCts != null || _followers.Count == 0) return;
            _tickCts = new CancellationTokenSource();
            RunFollowLoop(_tickCts.Token).Forget();
        }

        private void StopTick()
        {
            _tickCts?.Cancel();
            _tickCts?.Dispose();
            _tickCts = null;
        }

        private async UniTaskVoid RunFollowLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                // Iterate by index: Reposition may close a popup (world anchor destroyed) and mutate the list.
                for (var i = _followers.Count - 1; i >= 0; i--)
                {
                    if (i < _followers.Count)
                        _followers[i].Reposition();
                }

                try { await UniTask.NextFrame(ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        // ── Helpers ──────────────────────────────────────────────

        private void OnPointerMove(PointerMoveEvent evt)
            => _cursorPanelPosition = new Vector2(evt.position.x, evt.position.y);

        private VisualElement GetLayerContainer(UILayer layer) => layer switch
        {
            UILayer.Screen => _screenLayer,
            UILayer.HUD => _hudLayer,
            UILayer.Popup => _popupLayer,
            UILayer.Overlay => _overlayLayer,
            _ => _popupLayer
        };

        private VisualElement RequireLayer(string name)
            => _root.Q(name) ?? throw new InvalidOperationException(
                $"UI root is missing required child '{name}'. PopupHost requires the standard " +
                "screen-layer / hud-layer / popup-layer / overlay-layer elements.");

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopTick();
            CloseAllImmediate();
            if (_pointerScreenPosition == null)
                _root.UnregisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
        }
    }
}
