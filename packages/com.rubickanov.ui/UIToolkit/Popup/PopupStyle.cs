namespace Rubickanov.UI.UIToolkit
{
    /// <summary>
    /// USS class names used by the flexible popup system. Reassign any field before the first popup
    /// is shown to point the engine at your own CSS hooks (mirrors <see cref="DialogStyle"/>).
    /// </summary>
    public static class PopupStyle
    {
        public static string Backdrop = "popup-backdrop";
        public static string Panel = "popup-panel";
        public static string Title = "popup-title";
        public static string Icon = "popup-icon";
        public static string Message = "popup-message";
        public static string Content = "popup-content";
        public static string Input = "popup-input";
        public static string Buttons = "popup-buttons";
        public static string Button = "popup-btn";
        public static string ButtonPrimary = "popup-btn--primary";
        public static string Close = "popup-close";

        // Modifiers
        public static string Modal = "popup--modal";
        public static string Passive = "popup--passive";
        public static string SideTop = "popup--side-top";
        public static string SideBottom = "popup--side-bottom";
        public static string SideLeft = "popup--side-left";
        public static string SideRight = "popup--side-right";

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
