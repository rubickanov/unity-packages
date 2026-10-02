using System;

namespace Rubickanov.UI
{
    /// <summary>How a popup interacts with input underneath it.</summary>
    public enum PopupBehaviour
    {
        /// <summary>No backdrop: the rest of the screen stays usable around the popup.</summary>
        Passive,

        /// <summary>
        /// A backdrop over the whole layer blocks input to everything below, and <see cref="IUIService.Back"/> stops at
        /// the popup whether or not it closes on it.
        /// </summary>
        Modal
    }

    /// <summary>Ways a popup closes besides its handle. Combine as flags.</summary>
    [Flags]
    public enum PopupCloseTriggers
    {
        None = 0,

        /// <summary>Clicking the modal backdrop (outside the panel) closes it. Modal only.</summary>
        ClickOutside = 1 << 0,

        /// <summary>Closes on <see cref="IUIService.Back"/>, which the game calls on its Escape key.</summary>
        Escape = 1 << 1
    }

    /// <summary>Why a popup closed, reported on <see cref="PopupResult"/>.</summary>
    public enum PopupCloseReason
    {
        /// <summary>Closed through its handle (<see cref="IPopupHandle.Close"/>), or by its content.</summary>
        Code,
        ClickOutside,
        Escape,

        /// <summary>Closed by a popup opened with <see cref="PopupBuilder.DismissOthers"/>.</summary>
        Replaced
    }

    /// <summary>Region of the layer a popup is pinned to (<see cref="PopupPlacement.Screen"/>).</summary>
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
