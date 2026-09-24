using System;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// Tooltips as a popup preset: a passive popup with the <see cref="PopupStyle.Tooltip"/> class below the element,
    /// opened after a hover delay and closed when the pointer leaves the element. It sits on the overlay layer, above
    /// modal dialogs and their content, whatever layer the element is on. For a 3D object, open a popup with
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
            return Attach(element, popups, delay, popup => popup.Message(text()));
        }

        /// <param name="content">Builds the tooltip's content each time it opens.</param>
        public static PopupManipulator AttachTooltip(this VisualElement element, IPopupService popups,
            Func<VisualElement> content, float delay = 0.3f)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            return Attach(element, popups, delay, popup => popup.Content(content));
        }

        private static PopupManipulator Attach(VisualElement element, IPopupService popups, float delay,
            Action<PopupBuilder> setContent)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (popups == null) throw new ArgumentNullException(nameof(popups));

            return element.AttachPopup(popups, popup => Configure(popup, element, setContent), delay);
        }

        internal static void Configure(PopupBuilder popup, VisualElement element, Action<PopupBuilder> setContent)
        {
            popup.At(PopupPlacement.AtElement(element, PopupSide.Bottom))
                .OnLayer(UILayer.Overlay)
                .Class(PopupStyle.Tooltip);
            setContent(popup);
        }
    }
}
