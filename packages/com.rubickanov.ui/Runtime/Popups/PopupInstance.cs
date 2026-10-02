using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// One open popup: a frame over its layer (the backdrop of a modal popup) holding the panel, the content built from
    /// a <see cref="PopupConfig"/>, its close triggers, pointer capture and back handler.
    /// </summary>
    internal sealed class PopupInstance : IPopupHandle
    {
        private readonly PopupHost _host;
        private readonly UniTaskCompletionSource<PopupResult> _completion = new();
        private readonly PopupConfig _config;

        private readonly VisualElement _frame;
        private readonly VisualElement _panel;
        private readonly IViewAnimation _animation;
        private readonly VisualElement _animationTarget;
        private View? _contentView;

        private IDisposable? _pointerCapture;
        private IDisposable? _backHandle;
        private CancellationTokenSource? _show;

        public bool IsOpen { get; private set; }
        public UniTask<PopupResult> Result => _completion.Task;
        public VisualElement Panel => _panel;

        private bool IsModal => _config.Behaviour == PopupBehaviour.Modal;

        public PopupInstance(PopupConfig config, VisualElement layer, PopupHost host)
        {
            _config = config;
            _host = host;

            _frame = new VisualElement { name = PopupStyle.Frame };
            _frame.AddToClassList(PopupStyle.Frame);
            Stretch(_frame);

            _panel = new VisualElement { name = PopupStyle.Panel };
            _panel.AddToClassList(PopupStyle.Panel);
            _frame.Add(_panel);

            try
            {
                Build();
            }
            catch
            {
                // The popup owns the content view model even when it never opened.
                DestroyContentView();
                config.ContentViewModel?.Dispose();
                throw;
            }

            var viewAnimation = ContentViewAnimation();
            _animation = config.Animation ?? viewAnimation ?? host.DefaultAnimation;
            _animationTarget = config.Animation == null && viewAnimation != null ? ContentViewTarget() : _panel;
            _contentView?.SetPopup(this);

            layer.Add(_frame);
            IsOpen = true;
            AcquireInput();

            _show = new CancellationTokenSource();
            PlayShowAsync(_show.Token).Forget();
        }

        private IViewAnimation? ContentViewAnimation()
        {
            var animation = _contentView?.ResolveAnimation();
            return animation == null || ReferenceEquals(animation, NoneAnimation.Instance) ? null : animation;
        }

        // The view's own target when it names an inner element; its root is the panel's content, so the panel moves.
        private VisualElement ContentViewTarget()
        {
            var target = _contentView!.ResolveAnimationTarget();
            return target == _contentView.Root ? _panel : target;
        }

        // ── Construction ─────────────────────────────────────────

        private void Build()
        {
            if (IsModal)
            {
                _frame.AddToClassList(PopupStyle.Backdrop);
                _frame.pickingMode = PickingMode.Position;
                if (Has(PopupCloseTriggers.ClickOutside))
                    _frame.RegisterCallback<PointerDownEvent>(OnBackdropPointerDown);
            }
            else
            {
                _frame.pickingMode = PickingMode.Ignore;
            }

            _panel.AddToClassList(IsModal ? PopupStyle.Modal : PopupStyle.Passive);
            foreach (var className in _config.Classes)
                _panel.AddToClassList(className);
            if (_config.StyleSheet != null)
                _panel.styleSheets.Add(_config.StyleSheet);

            Place(_config.Placement);

            if (_config.ContentFactory != null)
            {
                var content = _config.ContentFactory()
                    ?? throw new InvalidOperationException("The popup's content factory returned null.");
                content.AddToClassList(PopupStyle.Content);
                _panel.Add(content);
            }
            else if (_config.ContentViewType != null)
            {
                var viewModel = _config.ContentViewModel ?? throw new InvalidOperationException(
                    $"Popup content view {_config.ContentViewType.Name} has no view model.");
                _contentView = _host.CreateContentView(_config.ContentViewType, viewModel);
                _contentView.Root.AddToClassList(PopupStyle.Content);
                _panel.Add(_contentView.Root);
            }

            if (_config.PassThrough)
            {
                _panel.pickingMode = PickingMode.Ignore;
                _panel.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
            }
        }

        /// <summary>Lays the panel out in the frame: stretched, or aligned to a region and moved by the offset.</summary>
        private void Place(in PopupPlacement placement)
        {
            if (placement.IsFill)
            {
                _panel.style.position = Position.Absolute;
                Stretch(_panel);
                return;
            }

            var corner = placement.Corner;
            var offset = placement.Offset;
            _frame.style.flexDirection = FlexDirection.Column;

            switch (corner)
            {
                case PopupAnchorCorner.TopLeft or PopupAnchorCorner.Top or PopupAnchorCorner.TopRight:
                    _frame.style.justifyContent = Justify.FlexStart;
                    _frame.style.paddingTop = offset.y;
                    break;
                case PopupAnchorCorner.BottomLeft or PopupAnchorCorner.Bottom or PopupAnchorCorner.BottomRight:
                    _frame.style.justifyContent = Justify.FlexEnd;
                    _frame.style.paddingBottom = offset.y;
                    break;
                default:
                    _frame.style.justifyContent = Justify.Center;
                    _panel.style.top = offset.y;
                    break;
            }

            switch (corner)
            {
                case PopupAnchorCorner.TopLeft or PopupAnchorCorner.Left or PopupAnchorCorner.BottomLeft:
                    _frame.style.alignItems = Align.FlexStart;
                    _frame.style.paddingLeft = offset.x;
                    break;
                case PopupAnchorCorner.TopRight or PopupAnchorCorner.Right or PopupAnchorCorner.BottomRight:
                    _frame.style.alignItems = Align.FlexEnd;
                    _frame.style.paddingRight = offset.x;
                    break;
                default:
                    _frame.style.alignItems = Align.Center;
                    _panel.style.left = offset.x;
                    break;
            }
        }

        private static void Stretch(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.top = 0;
            element.style.right = 0;
            element.style.bottom = 0;
        }

        // ── Input ────────────────────────────────────────────────

        private void AcquireInput()
        {
            if (IsModal || !_config.PassThrough)
                _pointerCapture = _host.Ui.CapturePointer();
            if (IsModal || Has(PopupCloseTriggers.Escape))
                _backHandle = _host.Ui.PushBackHandler(OnBack);
        }

        private void ReleaseInput()
        {
            var capture = _pointerCapture;
            var back = _backHandle;
            _pointerCapture = null;
            _backHandle = null;
            capture?.Dispose();
            back?.Dispose();
        }

        // ── Animation ────────────────────────────────────────────

        private async UniTaskVoid PlayShowAsync(CancellationToken ct)
        {
            try
            {
                await _animation.PlayShowAsync(_animationTarget, ct);
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
                await _animation.PlayHideAsync(_animationTarget, CancellationToken.None);
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
            _frame.RemoveFromHierarchy();
            DestroyContentView();
        }

        /// <summary>Destroys the content view, which unbinds and disposes its view model.</summary>
        private void DestroyContentView()
        {
            var view = _contentView;
            _contentView = null;
            try
            {
                view?.Destroy();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        // ── Close triggers ───────────────────────────────────────

        // A modal popup takes every Back: nothing under it may answer while it is up, even when it does not close.
        private bool OnBack()
        {
            // The content view may consume Back itself (close its own sub-panel) before the popup closes.
            if (_contentView != null && _contentView.HandleBack()) return true;
            if (Has(PopupCloseTriggers.Escape)) Close(null, PopupCloseReason.Escape, animate: true);
            return true;
        }

        private void OnBackdropPointerDown(PointerDownEvent evt)
        {
            // Only a click on the backdrop itself (outside the panel) dismisses.
            if (evt.target == _frame)
                Close(null, PopupCloseReason.ClickOutside, animate: true);
        }

        // ── IPopupHandle ─────────────────────────────────────────

        public void Close(string? id = null) => Close(id, PopupCloseReason.Code, animate: true);

        public void Dispose() => Close();

        internal void Close(PopupCloseReason reason) => Close(null, reason, animate: true);

        /// <summary>Closes and removes the elements at once, without the hide animation.</summary>
        internal void CloseImmediate() => Close(null, PopupCloseReason.Code, animate: false);

        /// <summary>Completes <see cref="Result"/> at once, plays the hide animation, then removes the elements.</summary>
        private void Close(string? id, PopupCloseReason reason, bool animate)
        {
            if (!IsOpen) return;
            IsOpen = false;

            ReleaseInput();

            var show = _show;
            _show = null;
            show?.Cancel();
            show?.Dispose();

            // The panel stays on screen while the hide animation plays: nothing in it may be clicked or submitted.
            // The elements are removed afterwards, so their picking needs no restoring.
            _frame.pickingMode = PickingMode.Ignore;
            _panel.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
            if (_panel.focusController?.focusedElement is VisualElement focused && _panel.Contains(focused))
                focused.Blur();

            _host.OnPopupClosed(this);
            _completion.TrySetResult(new PopupResult(id, reason));

            if (animate)
                PlayHideAndRemoveAsync().Forget();
            else
                RemoveElements();
        }

        private bool Has(PopupCloseTriggers trigger) => (_config.CloseTriggers & trigger) != 0;
    }
}
