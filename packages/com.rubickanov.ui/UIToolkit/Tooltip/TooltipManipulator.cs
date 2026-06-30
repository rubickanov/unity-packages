using System;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public class TooltipManipulator : Manipulator
    {
        private readonly TooltipService _service;
        private readonly string? _text;
        private readonly Func<string>? _textFactory;
        private readonly Func<VisualElement>? _contentFactory;
        private readonly float _delay;

        private IVisualElementScheduledItem? _scheduledShow;
        private bool _isShowing;
        private bool _cancelled;

        public TooltipManipulator(TooltipService service, string text, float delay = 0.3f)
        {
            _service = service;
            _text = text;
            _delay = delay;
        }

        public TooltipManipulator(TooltipService service, Func<string> textFactory, float delay = 0.3f)
        {
            _service = service;
            _textFactory = textFactory;
            _delay = delay;
        }

        public TooltipManipulator(TooltipService service, Func<VisualElement> contentFactory, float delay = 0.3f)
        {
            _service = service;
            _contentFactory = contentFactory;
            _delay = delay;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            target.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            CancelScheduledShow();
            if (_isShowing) _service.Hide();

            target.UnregisterCallback<PointerEnterEvent>(OnPointerEnter);
            target.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
        }

        private void OnPointerEnter(PointerEnterEvent evt)
        {
            CancelScheduledShow();

            if (_delay <= 0f)
            {
                // CancelScheduledShow() above set _cancelled = true; clear it so the immediate
                // ShowTooltip() isn't swallowed by its own `if (_cancelled) return;` guard.
                _cancelled = false;
                ShowTooltip();
                return;
            }

            _cancelled = false;
            _scheduledShow = target.schedule.Execute(ShowTooltip).StartingIn((long)(_delay * 1000f));
        }

        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            CancelScheduledShow();

            if (_isShowing)
            {
                _service.Hide();
                _isShowing = false;
            }
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_isShowing)
                _service.UpdatePosition(target);
        }

        private void ShowTooltip()
        {
            _scheduledShow = null;
            if (_cancelled) return;

            if (_contentFactory != null)
                _service.Show(target, _contentFactory());
            else if (_textFactory != null)
                _service.Show(target, _textFactory());
            else
                _service.Show(target, _text!);

            _isShowing = true;
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
