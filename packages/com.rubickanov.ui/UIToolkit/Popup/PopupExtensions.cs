using System;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    /// <summary>Hover entry points for the flexible popup system (mirrors <see cref="TooltipExtensions"/>).</summary>
    public static class PopupExtensions
    {
        /// <summary>
        /// Shows a fully custom popup while hovering this element. The factory controls placement,
        /// content and behaviour; anchor it to the element with <see cref="PopupPlacement.AtElement"/>.
        /// </summary>
        public static PopupManipulator AttachPopup(this VisualElement element, IPopupService service,
            Func<PopupConfig> configFactory, float delay = 0.3f)
        {
            var manipulator = new PopupManipulator(service, configFactory, delay);
            element.AddManipulator(manipulator);
            return manipulator;
        }

        /// <summary>
        /// Convenience: shows a passive title/message popup anchored below this element on hover,
        /// auto-flipping near screen edges. Closes when the pointer leaves.
        /// </summary>
        public static PopupManipulator AttachPopup(this VisualElement element, IPopupService service,
            string title, string? message = null, float delay = 0.3f)
        {
            return element.AttachPopup(service, () => new PopupConfig
            {
                Title = title,
                Message = message,
                Placement = PopupPlacement.AtElement(element),
                Behaviour = PopupBehaviour.Passive,
                CloseTriggers = PopupCloseTriggers.PointerLeave
            }, delay);
        }

        public static void RemovePopup(this VisualElement element, PopupManipulator manipulator)
            => element.RemoveManipulator(manipulator);
    }
}
