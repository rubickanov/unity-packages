namespace Rubickanov.UI
{
    public static class PopupExtensions
    {
        /// <summary>
        /// Shows a new instance of the popup view <typeparamref name="TView"/> (a registered view on
        /// <see cref="UILayer.Popup"/>) bound to <paramref name="viewModel"/>: it covers the popup layer, captures the
        /// pointer and closes on <see cref="IUIService.Back"/> unless its <c>OnBack</c> consumes it. The popup owns the
        /// view and the view model; the view reaches the popup through <see cref="View.Popup"/> and closes it with its
        /// answer (<c>Popup.Close("yes")</c>).
        /// </summary>
        public static IPopupHandle ShowView<TView, TViewModel>(this IPopupService popups, TViewModel viewModel)
            where TView : View<TViewModel>
            where TViewModel : ViewModelBase =>
            popups.Create()
                .Content<TView, TViewModel>(viewModel)
                .At(PopupPlacement.Fill())
                .OnLayer(UILayer.Popup)
                .Class(PopupStyle.View)
                .CloseOn(PopupCloseTriggers.Escape)
                .Open();
    }
}
