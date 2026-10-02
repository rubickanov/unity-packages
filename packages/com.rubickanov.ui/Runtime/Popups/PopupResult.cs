namespace Rubickanov.UI
{
    /// <summary>Outcome of a popup, awaited via <see cref="IPopupHandle.Result"/>.</summary>
    public sealed class PopupResult
    {
        /// <summary>The id given to <see cref="IPopupHandle.Close"/>: the answer the content chose. Null otherwise.</summary>
        public string? Id { get; }

        /// <summary>Why the popup closed.</summary>
        public PopupCloseReason Reason { get; }

        public PopupResult(string? id, PopupCloseReason reason)
        {
            Id = id;
            Reason = reason;
        }
    }
}
