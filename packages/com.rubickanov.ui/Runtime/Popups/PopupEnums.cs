using System;

namespace Rubickanov.UI.UIToolkit
{
    /// <summary>How a popup interacts with input underneath it.</summary>
    public enum PopupBehaviour
    {
        /// <summary>No backdrop; input passes through to whatever is below (tooltips, toasts).</summary>
        Passive,

        /// <summary>Full-layer backdrop that blocks input to lower layers (modal dialogs).</summary>
        Modal
    }

    /// <summary>Ways a popup can be dismissed. Combine as flags.</summary>
    [Flags]
    public enum PopupCloseTriggers
    {
        None = 0,

        /// <summary>Render an X button in the corner that closes the popup.</summary>
        CloseButton = 1 << 0,

        /// <summary>Any content button closes the popup when clicked.</summary>
        ActionButton = 1 << 1,

        /// <summary>Clicking the modal backdrop (outside the panel) closes it. Modal only.</summary>
        ClickOutside = 1 << 2,

        /// <summary>Pressing Escape closes the topmost popup. Requires panel focus.</summary>
        Escape = 1 << 3,

        /// <summary>Closes when the pointer leaves the panel (hover dismiss).</summary>
        PointerLeave = 1 << 4,

        /// <summary>Auto-closes after <see cref="PopupConfig.TimeoutSeconds"/>.</summary>
        Timeout = 1 << 5
    }

    /// <summary>Why a popup closed, reported on <see cref="PopupResult"/>.</summary>
    public enum PopupCloseReason
    {
        Button,
        CloseButton,
        ClickOutside,
        Escape,
        PointerLeave,
        Timeout,
        Code,
        Replaced
    }

    /// <summary>How a popup is anchored on screen.</summary>
    public enum PopupPlacementMode
    {
        /// <summary>Pinned to a region of the screen (e.g. top-right toast).</summary>
        ScreenAnchor,

        /// <summary>Centered on an explicit panel-space point.</summary>
        ScreenPoint,

        /// <summary>Next to a UI <see cref="UnityEngine.UIElements.VisualElement"/>.</summary>
        Element,

        /// <summary>Above a world-space object, following the camera each frame.</summary>
        World,

        /// <summary>Follows the mouse cursor.</summary>
        Cursor
    }

    /// <summary>Which side of the anchor an element-placed popup sits on.</summary>
    public enum PopupSide
    {
        Top,
        Bottom,
        Left,
        Right
    }

    /// <summary>Region of the screen for <see cref="PopupPlacementMode.ScreenAnchor"/>.</summary>
    public enum PopupAnchorCorner
    {
        TopLeft,
        Top,
        TopRight,
        Left,
        Center,
        Right,
        BottomLeft,
        Bottom,
        BottomRight
    }
}
