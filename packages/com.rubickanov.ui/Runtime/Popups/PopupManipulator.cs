using System;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// Shows a popup while the pointer hovers the target element, after a delay. Closes it when the pointer leaves or
    /// the target leaves the panel (a rebuilt list row gets no <see cref="PointerLeaveEvent"/>).
    /// </summary>
    public sealed class PopupManipulator : Manipulator
    {
        private readonly IPopupService _service;
        private readonly Action<PopupBuilder> _configure;
        private readonly float _delay;

        private IVisualElementScheduledItem? _scheduledShow;
        private IPopupHandle? _handle;

        /// <param name="configure">Describes the popup each time it opens.</param>
        public PopupManipulator(IPopupService service, Action<PopupBuilder> configure, float delay = 0.3f)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _configure = configure ?? throw new ArgumentNullException(nameof(configure));
            _delay = delay;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            target.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            CancelScheduledShow();
            CloseCurrent();
            target.UnregisterCallback<PointerEnterEvent>(OnPointerEnter);
            target.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        private void OnPointerEnter(PointerEnterEvent evt)
        {
            CancelScheduledShow();

            if (_delay <= 0f)
            {
                ShowPopup();
                return;
            }

            _scheduledShow = target.schedule.Execute(ShowPopup).StartingIn((long)(_delay * 1000f));
        }

        private void OnPointerLeave(PointerLeaveEvent evt) => Hide();

        private void OnDetach(DetachFromPanelEvent evt) => Hide();

        private void Hide()
        {
            CancelScheduledShow();
            CloseCurrent();
        }

        private void ShowPopup()
        {
            _scheduledShow = null;
            CloseCurrent();
            var popup = _service.Create();
            _configure(popup);
            _handle = popup.Open();
        }

        private void CloseCurrent()
        {
            if (_handle is { IsOpen: true })
                _handle.Close(null, PopupCloseReason.PointerLeave);
            _handle = null;
        }

        private void CancelScheduledShow()
        {
            _scheduledShow?.Pause();
            _scheduledShow = null;
        }
    }
}
