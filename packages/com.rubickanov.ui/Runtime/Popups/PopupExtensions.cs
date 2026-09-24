using System;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>Hover entry points for the flexible popup system.</summary>
    public static class PopupExtensions
    {
        /// <summary>
        /// Shows a fully custom popup while hovering this element. The factory controls placement,
        /// content and behaviour; anchor it to the element with <see cref="PopupPlacement.AtElement"/>.
        /// For a text hint use <see cref="TooltipExtensions.AttachTooltip(VisualElement, IPopupService, string, float)"/>.
        /// </summary>
        public static PopupManipulator AttachPopup(this VisualElement element, IPopupService service,
            Func<PopupConfig> configFactory, float delay = 0.3f)
        {
            var manipulator = new PopupManipulator(service, configFactory, delay);
            element.AddManipulator(manipulator);
            return manipulator;
        }

        public static void RemovePopup(this VisualElement element, PopupManipulator manipulator)
            => element.RemoveManipulator(manipulator);
    }
}
