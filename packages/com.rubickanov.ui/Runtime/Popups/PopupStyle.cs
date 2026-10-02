namespace Rubickanov.UI
{
    /// <summary>USS class names of the popup elements. The package sets layout only; how a popup looks is the game's.</summary>
    public static class PopupStyle
    {
        /// <summary>The element over the whole layer that holds the panel; the backdrop of a modal popup.</summary>
        public const string Frame = "popup-frame";

        /// <summary>On the frame of a modal popup: the dim behind it.</summary>
        public const string Backdrop = "popup-backdrop";

        public const string Panel = "popup-panel";

        /// <summary>On the content inside the panel: the built element or the view's root.</summary>
        public const string Content = "popup-content";

        // Modifiers on the panel
        public const string Modal = "popup--modal";
        public const string Passive = "popup--passive";

        /// <summary>A popup view (<c>ShowView</c>): stretched over the layer, the view draws itself.</summary>
        public const string View = "popup--view";
    }
}
