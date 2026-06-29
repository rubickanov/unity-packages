using System;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    /// <summary>
    /// Shows a popup while the pointer hovers the target element, after a delay. Mirrors
    /// <see cref="TooltipManipulator"/> but opens a full <see cref="PopupConfig"/> via <see cref="IPopupService"/>.
    /// </summary>
    public sealed class PopupManipulator : Manipulator
    {
        private readonly IPopupService _service;
        private readonly Func<PopupConfig> _configFactory;
        private readonly float _delay;

        private IVisualElementScheduledItem? _scheduledShow;
        private IPopupHandle? _handle;
        private bool _cancelled;

        public PopupManipulator(IPopupService service, Func<PopupConfig> configFactory, float delay = 0.3f)
        {
            _service = service;
            _configFactory = configFactory;
            _delay = delay;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            target.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            CancelScheduledShow();
            CloseCurrent();
            target.UnregisterCallback<PointerEnterEvent>(OnPointerEnter);
            target.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
        }

        private void OnPointerEnter(PointerEnterEvent evt)
        {
            CancelScheduledShow();

            if (_delay <= 0f)
            {
                ShowPopup();
                return;
            }

            _cancelled = false;
            _scheduledShow = target.schedule.Execute(ShowPopup).StartingIn((long)(_delay * 1000f));
        }

        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            CancelScheduledShow();
            CloseCurrent();
        }

        private void ShowPopup()
        {
            _scheduledShow = null;
            if (_cancelled) return;

            CloseCurrent();
            _handle = _service.Open(_configFactory());
        }

        private void CloseCurrent()
        {
            if (_handle is { IsOpen: true })
                _handle.Close(null, PopupCloseReason.PointerLeave);
            _handle = null;
        }

        private void CancelScheduledShow()
        {
            _cancelled = true;
            if (_scheduledShow != null)
            {
                _scheduledShow.Pause();
                _scheduledShow = null;
            }
        }
    }
}
