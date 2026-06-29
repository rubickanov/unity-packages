namespace Rubickanov.UI.UIToolkit
{
    /// <summary>
    /// Shows flexible popups: configurable panels placed anywhere on screen, modal or passive,
    /// with configurable dismiss rules. Multiple popups can be open at once.
    /// </summary>
    public interface IPopupService
    {
        /// <summary>Opens a popup from a fully-built config and returns a handle to control it.</summary>
        IPopupHandle Open(PopupConfig config);

        /// <summary>Starts a fluent builder for a popup.</summary>
        PopupBuilder Create();

        /// <summary>Closes every open popup.</summary>
        void CloseAll();
    }
}
