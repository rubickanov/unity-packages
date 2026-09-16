using System;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// Tooltips as a popup preset: a passive popup with the <see cref="PopupStyle.Tooltip"/> class below the element,
    /// opened after a hover delay and closed when the pointer leaves the element. For a 3D object, open a popup with
    /// <see cref="PopupPlacement.Cursor"/> or <see cref="PopupPlacement.AtWorld"/> and close its handle.
    /// </summary>
    public static class TooltipExtensions
    {
        public static PopupManipulator AttachTooltip(this VisualElement element, IPopupService popups, string text,
            float delay = 0.3f)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return element.AttachTooltip(popups, () => text, delay);
        }

        /// <param name="text">Evaluated each time the tooltip opens.</param>
        public static PopupManipulator AttachTooltip(this VisualElement element, IPopupService popups, Func<string> text,
            float delay = 0.3f)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return Attach(element, popups, delay, config => config.Message = text());
        }

        /// <param name="content">Builds the tooltip's content each time it opens.</param>
        public static PopupManipulator AttachTooltip(this VisualElement element, IPopupService popups,
            Func<VisualElement> content, float delay = 0.3f)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            return Attach(element, popups, delay, config => config.ContentFactory = content);
        }

        private static PopupManipulator Attach(VisualElement element, IPopupService popups, float delay,
            Action<PopupConfig> setContent)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (popups == null) throw new ArgumentNullException(nameof(popups));

            return element.AttachPopup(popups, () =>
            {
                var config = new PopupConfig
                {
                    Placement = PopupPlacement.AtElement(element, PopupSide.Bottom),
                    Behaviour = PopupBehaviour.Passive,
                    CloseTriggers = PopupCloseTriggers.None,
                    RootClass = PopupStyle.Tooltip
                };
                setContent(config);
                return config;
            }, delay);
        }
    }
}
