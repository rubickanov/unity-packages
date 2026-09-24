namespace Rubickanov.UI
{
    public interface IPopupService
    {
        /// <summary>Starts describing a popup; <see cref="PopupBuilder.Open"/> shows it.</summary>
        PopupBuilder Create();

        /// <summary>Shows the popup <paramref name="popup"/> describes. <see cref="PopupBuilder.Open"/> calls it.</summary>
        IPopupHandle Open(PopupBuilder popup);

        /// <summary>Closes every open popup.</summary>
        void CloseAll();
    }
}
