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
    /// supports many open at once, and drives world/cursor followers from one MonoBehaviour-free loop.
    /// </summary>
    public sealed class PopupHost : IPopupService, IDisposable
    {
        private readonly VisualElement _root;
        private readonly UILayerElements _layers;
        private readonly StyleSheet? _defaultStyleSheet;

        private readonly List<PopupInstance> _open = new();
        private readonly List<PopupInstance> _followers = new();

        private readonly UIService _ui;
        private readonly Func<Vector2>? _pointerScreenPosition;
        private readonly Func<Camera?>? _cameraProvider;
        private readonly IViewAnimation _defaultAnimation;

        private Camera? _worldCamera;
        private int _worldCameraFrame = -1;

        private CancellationTokenSource? _tickCts;
        private Vector2 _cursorPanelPosition;
        private bool _disposed;

        internal int FollowerCount => _followers.Count;

        /// <param name="root">Element holding the standard layer elements (for example <c>uiDocument.rootVisualElement</c>).</param>
        /// <param name="ui">
        /// Receives a pointer capture for every open modal or interactive popup and a back handler for every popup
        /// closing on <see cref="PopupCloseTriggers.Escape"/>; builds the popup views and views as content.
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
        public PopupHost(VisualElement root, UIService ui, StyleSheet? defaultStyleSheet = null,
            Func<Vector2>? pointerScreenPosition = null, Func<Camera?>? camera = null, IViewAnimation? animation = null)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _cameraProvider = camera;
            _defaultAnimation = animation ?? NoneAnimation.Instance;
            _layers = new UILayerElements(root);
            _pointerScreenPosition = pointerScreenPosition;

            _defaultStyleSheet = defaultStyleSheet;
            if (_defaultStyleSheet != null)
                _root.styleSheets.Add(_defaultStyleSheet);

            // Event-based fallback only when no live provider is supplied.
            if (_pointerScreenPosition == null)
                _root.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
        }

        // ── IPopupService ────────────────────────────────────────

        public IPopupHandle Open(PopupBuilder popup)
        {
            if (popup == null) throw new ArgumentNullException(nameof(popup));
            if (_disposed)
                throw new ObjectDisposedException(nameof(PopupHost));

            var config = popup.Config;
            if (config.DismissOthers)
                CloseAll(PopupCloseReason.Replaced);

            var layer = _layers[config.ResolveLayer()];
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

        public void CloseAll() => CloseAll(PopupCloseReason.Code);

        private void CloseAll(PopupCloseReason reason)
        {
            // Copy: Close mutates _open via the callback.
            var snapshot = _open.ToArray();
            foreach (var popup in snapshot)
                popup.Close(null, reason);
        }

        private void CloseAllImmediate()
        {
            var snapshot = _open.ToArray();
            foreach (var popup in snapshot)
                popup.CloseImmediate();
        }

        // ── For PopupInstance ────────────────────────────────────

        internal Vector2 CursorPanelPosition =>
            _pointerScreenPosition != null
                ? PopupPlacementResolver.ScreenToPanel(_root, _pointerScreenPosition())
                : _cursorPanelPosition;

        /// <summary>Camera for world placements without their own camera; looked up once per frame.</summary>
        internal Camera? WorldCamera
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

        internal UIService Ui => _ui;
        internal IViewAnimation DefaultAnimation => _defaultAnimation;

        /// <summary>A new instance of a registered view, bound and shown, for the popup to own.</summary>
        internal View CreateContentView(Type viewType, ViewModelBase viewModel) =>
            _ui.CreateDetached(viewType, viewModel);

        internal void OnPopupClosed(PopupInstance instance)
        {
            _open.Remove(instance);
            _followers.Remove(instance);
            if (_followers.Count == 0)
                StopTick();
        }

        internal void OnPlacementChanged(PopupInstance instance)
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

        // ── Follower loop ────────────────────────────────────────

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
                RepositionFollowers();

                // After the UI Toolkit panel update and LateUpdate (a following camera moves there), before the
                // repaint: the popup lands where the camera is this frame, not where it was the last one.
                try { await UniTask.NextFrame(PlayerLoopTiming.LastPreLateUpdate, ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        /// <summary>One frame of the follower loop.</summary>
        internal void RepositionFollowers()
        {
            // Iterate by index: Reposition may close a popup (world anchor destroyed) and mutate the list.
            for (var i = _followers.Count - 1; i >= 0; i--)
            {
                if (i >= _followers.Count) continue;
                // One failing popup (a throwing pointer provider) must not stop the loop for the others.
                try { _followers[i].Reposition(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        // ── Helpers ──────────────────────────────────────────────

        private void OnPointerMove(PointerMoveEvent evt)
            => _cursorPanelPosition = new Vector2(evt.position.x, evt.position.y);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopTick();
            CloseAllImmediate();
            if (_pointerScreenPosition == null)
                _root.UnregisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            if (_defaultStyleSheet != null)
                _root.styleSheets.Remove(_defaultStyleSheet);
        }
    }
}
