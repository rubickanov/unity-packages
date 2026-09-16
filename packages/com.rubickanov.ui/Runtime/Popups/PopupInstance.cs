using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>Callbacks a <see cref="PopupInstance"/> needs from its host.</summary>
    internal interface IPopupHostCallbacks
    {
        /// <summary>Latest pointer position in panel space (for cursor placement).</summary>
        Vector2 CursorPanelPosition { get; }

        /// <summary>Camera for world placements without their own camera; looked up once per frame.</summary>
        Camera? WorldCamera { get; }

        IUIService Ui { get; }
        IViewAnimation DefaultAnimation { get; }

        void OnPopupClosed(PopupInstance instance);
        void OnPlacementChanged(PopupInstance instance);
    }

    /// <summary>
    /// One open popup: owns its backdrop (modal only) and panel elements, builds content from a
    /// <see cref="PopupConfig"/>, wires close triggers, and positions itself via <see cref="PopupPlacementResolver"/>.
    /// </summary>
    internal sealed class PopupInstance : IPopupHandle
    {
        private readonly VisualElement _layer;
        private readonly IPopupHostCallbacks _host;
        private readonly UniTaskCompletionSource<PopupResult> _completion = new();
        private readonly IViewAnimation _animation;

        private PopupConfig _config;
        private PopupPlacement _placement;

        private VisualElement? _backdrop;
        private readonly VisualElement _panel;
        private Label? _title;
        private Label? _message;
        private VisualElement? _icon;
        private TextField? _input;
        private bool _interactive;

        private IVisualElementScheduledItem? _timeout;
        private PopupSide? _appliedSide;

        private IDisposable? _pointerCapture;
        private IDisposable? _backHandle;
        private CancellationTokenSource? _show;

        public bool IsOpen { get; private set; }
        public UniTask<PopupResult> Result => _completion.Task;
        public bool IsFollower => _placement.IsFollower;
        public bool IsModal => _config.Behaviour == PopupBehaviour.Modal;

        public PopupInstance(PopupConfig config, VisualElement layer, IPopupHostCallbacks host)
        {
            _config = config;
            _placement = config.Placement;
            _layer = layer;
            _host = host;
            _animation = config.Animation ?? host.DefaultAnimation;

            _panel = new VisualElement { name = "popup-panel" };
            _panel.AddToClassList(PopupStyle.Panel);
            _panel.style.position = Position.Absolute;

            Build();
            Attach();
            IsOpen = true;

            if (IsModal || _interactive)
                _pointerCapture = host.Ui.CapturePointer();
            if (Has(PopupCloseTriggers.Escape))
                _backHandle = host.Ui.PushBackHandler(OnBack);

            _show = new CancellationTokenSource();
            PlayShowAsync(_show.Token).Forget();
        }

        // ── Construction ─────────────────────────────────────────

        private void Build()
        {
            var modal = _config.Behaviour == PopupBehaviour.Modal;

            if (modal)
            {
                _backdrop = new VisualElement { name = "popup-backdrop" };
                _backdrop.AddToClassList(PopupStyle.Backdrop);
                _backdrop.pickingMode = PickingMode.Position;
                if (Has(PopupCloseTriggers.ClickOutside))
                {
                    _backdrop.RegisterCallback<PointerDownEvent>(OnBackdropPointerDown);
                }
            }

            _panel.AddToClassList(modal ? PopupStyle.Modal : PopupStyle.Passive);
            if (!string.IsNullOrEmpty(_config.RootClass))
                _panel.AddToClassList(_config.RootClass);
            if (_config.StyleSheet != null)
                _panel.styleSheets.Add(_config.StyleSheet);

            if (Has(PopupCloseTriggers.CloseButton))
            {
                var close = new Button(() => Close(null, PopupCloseReason.CloseButton)) { text = "✕" };
                close.AddToClassList(PopupStyle.Close);
                _panel.Add(close);
            }

            if (!string.IsNullOrEmpty(_config.Title))
            {
                _title = new Label(_config.Title);
                _title.AddToClassList(PopupStyle.Title);
                _panel.Add(_title);
            }

            if (_config.Icon != null)
            {
                _icon = new VisualElement();
                _icon.AddToClassList(PopupStyle.Icon);
                _icon.style.backgroundImage = new StyleBackground(_config.Icon);
                _panel.Add(_icon);
            }

            if (!string.IsNullOrEmpty(_config.Message))
            {
                _message = new Label(_config.Message);
                _message.AddToClassList(PopupStyle.Message);
                _message.style.whiteSpace = WhiteSpace.Normal;
                _panel.Add(_message);
            }

            if (_config.ContentFactory != null)
            {
                var content = _config.ContentFactory();
                content.AddToClassList(PopupStyle.Content);
                _panel.Add(content);
            }

            if (_config.HasInput)
            {
                _input = new TextField();
                _input.AddToClassList(PopupStyle.Input);
                _input.value = _config.InputDefault;
                _input.textEdition.placeholder = _config.InputPlaceholder;
                _panel.Add(_input);
            }

            if (_config.Buttons.Count > 0)
            {
                var row = new VisualElement();
                row.AddToClassList(PopupStyle.Buttons);
                foreach (var btn in _config.Buttons)
                {
                    var id = btn.Id;
                    var closes = btn.ClosesOnClick || Has(PopupCloseTriggers.ActionButton);
                    var button = new Button(() => { if (closes) Close(id, PopupCloseReason.Button); })
                    {
                        text = btn.Text
                    };
                    button.AddToClassList(PopupStyle.Button);
                    if (btn.IsPrimary)
                        button.AddToClassList(PopupStyle.ButtonPrimary);
                    row.Add(button);
                }
                _panel.Add(row);
            }

            if (Has(PopupCloseTriggers.PointerLeave))
            {
                _panel.RegisterCallback<PointerLeaveEvent>(OnPanelPointerLeave);
            }

            // A purely informational passive popup (no buttons / input / hover-close) should let clicks
            // pass through to the game underneath, like a tooltip. Anything interactive stays pickable
            // and captures the pointer.
            _interactive = _config.Buttons.Count > 0
                           || Has(PopupCloseTriggers.CloseButton)
                           || _config.HasInput
                           || Has(PopupCloseTriggers.PointerLeave);
            if (!modal && !_interactive)
            {
                _panel.pickingMode = PickingMode.Ignore;
                _panel.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
            }

            // Re-resolve once the panel has a real measured size (avoids NaN before first layout).
            _panel.RegisterCallback<GeometryChangedEvent>(OnPanelGeometryChanged);
        }

        private void Attach()
        {
            if (_backdrop != null) _layer.Add(_backdrop);
            _layer.Add(_panel);
            Reposition();

            if (_config.TimeoutSeconds > 0f && Has(PopupCloseTriggers.Timeout))
            {
                _timeout = _panel.schedule
                    .Execute(() => Close(null, PopupCloseReason.Timeout))
                    .StartingIn((long)(_config.TimeoutSeconds * 1000f));
            }
        }

        // ── Animation ────────────────────────────────────────────

        private async UniTaskVoid PlayShowAsync(CancellationToken ct)
        {
            try
            {
                await _animation.PlayShowAsync(_panel, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private async UniTaskVoid PlayHideAndRemoveAsync()
        {
            try
            {
                await _animation.PlayHideAsync(_panel, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                RemoveElements();
            }
        }

        private void RemoveElements()
        {
            _backdrop?.RemoveFromHierarchy();
            _panel.RemoveFromHierarchy();
        }

        // ── Positioning ──────────────────────────────────────────

        public void Reposition()
        {
            if (!IsOpen) return;

            var size = new Vector2(_panel.resolvedStyle.width, _panel.resolvedStyle.height);
            var camera = _placement.Mode == PopupPlacementMode.World && _placement.Camera == null
                ? _host.WorldCamera
                : null;
            var visible = PopupPlacementResolver.TryResolve(
                _layer, _placement, size, _host.CursorPanelPosition, camera, out var topLeft, out var side);

            if (!visible)
            {
                _panel.style.display = DisplayStyle.None;
                return;
            }

            _panel.style.display = DisplayStyle.Flex;
            _panel.style.left = topLeft.x;
            _panel.style.top = topLeft.y;

            if (_placement.Mode == PopupPlacementMode.Element && side != _appliedSide)
            {
                if (_appliedSide.HasValue)
                    _panel.RemoveFromClassList(PopupStyle.SideClass(_appliedSide.Value));
                _panel.AddToClassList(PopupStyle.SideClass(side));
                _appliedSide = side;
            }
        }

        private void OnPanelGeometryChanged(GeometryChangedEvent _) => Reposition();

        // ── Close triggers ───────────────────────────────────────

        private bool OnBack()
        {
            Close(null, PopupCloseReason.Escape);
            return true;
        }

        private void OnBackdropPointerDown(PointerDownEvent evt)
        {
            // Only a click on the backdrop itself (outside the panel) dismisses.
            if (evt.target == _backdrop)
                Close(null, PopupCloseReason.ClickOutside);
        }

        private void OnPanelPointerLeave(PointerLeaveEvent _) => Close(null, PopupCloseReason.PointerLeave);

        // ── IPopupHandle ─────────────────────────────────────────

        /// <summary>Completes <see cref="Result"/> at once, plays the hide animation, then removes the elements.</summary>
        public void Close(string? buttonId = null, PopupCloseReason reason = PopupCloseReason.Code)
            => Close(buttonId, reason, animate: true);

        /// <summary>Closes and removes the elements at once, without the hide animation.</summary>
        internal void CloseImmediate() => Close(null, PopupCloseReason.Code, animate: false);

        private void Close(string? buttonId, PopupCloseReason reason, bool animate)
        {
            if (!IsOpen) return;
            IsOpen = false;

            _timeout?.Pause();
            _timeout = null;

            var input = _config.HasInput ? _input?.value : null;

            var capture = _pointerCapture;
            var back = _backHandle;
            _pointerCapture = null;
            _backHandle = null;
            capture?.Dispose();
            back?.Dispose();

            var show = _show;
            _show = null;
            show?.Cancel();
            show?.Dispose();

            _panel.pickingMode = PickingMode.Ignore;
            if (_backdrop != null) _backdrop.pickingMode = PickingMode.Ignore;

            _host.OnPopupClosed(this);
            _completion.TrySetResult(new PopupResult(buttonId, reason, input));

            if (animate)
                PlayHideAndRemoveAsync().Forget();
            else
                RemoveElements();
        }

        public void UpdateContent(Action<PopupContentContext> mutate)
        {
            if (!IsOpen) return;
            mutate(new PopupContentContext(_panel, _title, _message, _icon));
            Reposition();
        }

        public void SetPlacement(in PopupPlacement placement)
        {
            _placement = placement;
            _host.OnPlacementChanged(this);
            Reposition();
        }

        private bool Has(PopupCloseTriggers trigger) => (_config.CloseTriggers & trigger) != 0;
    }
}
