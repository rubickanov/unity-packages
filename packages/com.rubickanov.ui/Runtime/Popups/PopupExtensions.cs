using System;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    public static class PopupExtensions
    {
        /// <summary>
        /// Shows a new instance of the popup view <typeparamref name="TView"/> (a registered view on
        /// <see cref="UILayer.Popup"/>) bound to <paramref name="viewModel"/>: it covers the popup layer, captures the
        /// pointer and closes on <see cref="IUIService.Back"/> unless its <c>OnBack</c> consumes it. The popup owns the
        /// view and the view model; the view reaches the popup through <see cref="View.Popup"/>.
        /// </summary>
        public static IPopupHandle ShowView<TView>(this IPopupService popups, ViewModelBase viewModel) where TView : View =>
            popups.Create()
                .Content<TView>(viewModel)
                .At(PopupPlacement.Fill())
                .OnLayer(UILayer.Popup)
                .Class(PopupStyle.View)
                .CloseOn(PopupCloseTriggers.Escape)
                .Open();

        /// <summary>
        /// Opens a popup while the pointer hovers this element, after <paramref name="delay"/> seconds, and closes it
        /// when the pointer leaves or the element leaves the panel. <paramref name="configure"/> describes the popup
        /// each time it opens; anchor it with <see cref="PopupPlacement.AtElement"/>. Remove it with
        /// <c>RemoveManipulator</c>. For a text hint use <see cref="TooltipExtensions"/>.
        /// </summary>
        public static PopupManipulator AttachPopup(this VisualElement element, IPopupService service,
            Action<PopupBuilder> configure, float delay = 0.3f)
        {
            var manipulator = new PopupManipulator(service, configure, delay);
            element.AddManipulator(manipulator);
            return manipulator;
        }
    }
}
