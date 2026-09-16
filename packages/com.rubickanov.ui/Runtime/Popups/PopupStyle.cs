namespace Rubickanov.UI
{
    /// <summary>
    /// USS class names used by the flexible popup system.
    /// </summary>
    public static class PopupStyle
    {
        public const string Backdrop = "popup-backdrop";
        public const string Panel = "popup-panel";
        public const string Title = "popup-title";
        public const string Icon = "popup-icon";
        public const string Message = "popup-message";
        public const string Content = "popup-content";
        public const string Input = "popup-input";
        public const string Buttons = "popup-buttons";
        public const string Button = "popup-btn";
        public const string ButtonPrimary = "popup-btn--primary";
        public const string Close = "popup-close";

        // Modifiers
        public const string Modal = "popup--modal";
        public const string Passive = "popup--passive";
        public const string SideTop = "popup--side-top";
        public const string SideBottom = "popup--side-bottom";
        public const string SideLeft = "popup--side-left";
        public const string SideRight = "popup--side-right";

        /// <summary>Maps a resolved side to its modifier class.</summary>
        public static string SideClass(PopupSide side) => side switch
        {
            PopupSide.Top => SideTop,
            PopupSide.Bottom => SideBottom,
            PopupSide.Left => SideLeft,
            PopupSide.Right => SideRight,
            _ => SideBottom
        };
    }
}
