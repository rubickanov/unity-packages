namespace Rubickanov.UI.UIToolkit
{
    /// <summary>Outcome of a popup, awaited via <see cref="IPopupHandle.Result"/>.</summary>
    public sealed class PopupResult
    {
        /// <summary>Id of the button that closed the popup, or null if it closed another way.</summary>
        public string? ButtonId { get; }

        /// <summary>Why the popup closed.</summary>
        public PopupCloseReason Reason { get; }

        /// <summary>Text of the input field, if the popup had one.</summary>
        public string? InputText { get; }

        public PopupResult(string? buttonId, PopupCloseReason reason, string? inputText)
        {
            ButtonId = buttonId;
            Reason = reason;
            InputText = inputText;
        }
    }
}
