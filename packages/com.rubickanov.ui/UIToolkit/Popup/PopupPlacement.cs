using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    /// <summary>
    /// Immutable description of where a popup is positioned. Build one with the static factories
    /// (<see cref="ScreenCenter"/>, <see cref="AtElement"/>, <see cref="AtWorld"/>, <see cref="Cursor"/> …)
    /// rather than the constructor.
    /// </summary>
    public readonly struct PopupPlacement
    {
        public readonly PopupPlacementMode Mode;

        // ScreenAnchor
        public readonly PopupAnchorCorner Corner;

        // ScreenPoint
        public readonly Vector2 ScreenPointPosition;

        // Element
        public readonly VisualElement? Element;
        public readonly PopupSide Side;
        public readonly bool AutoFlip;

        // World
        public readonly Transform? WorldAnchor;
        public readonly Camera? Camera;

        public readonly Vector2 Offset;

        private PopupPlacement(
            PopupPlacementMode mode,
            PopupAnchorCorner corner,
            Vector2 screenPointPosition,
            VisualElement? element,
            PopupSide side,
            bool autoFlip,
            Transform? worldAnchor,
            Camera? camera,
            Vector2 offset)
        {
            Mode = mode;
            Corner = corner;
            ScreenPointPosition = screenPointPosition;
            Element = element;
            Side = side;
            AutoFlip = autoFlip;
            WorldAnchor = worldAnchor;
            Camera = camera;
            Offset = offset;
        }

        /// <summary>True for placements that must be re-evaluated every frame.</summary>
        public bool IsFollower => Mode == PopupPlacementMode.World || Mode == PopupPlacementMode.Cursor;

        /// <summary>Centered on the screen.</summary>
        public static PopupPlacement ScreenCenter(Vector2 offset = default)
            => Screen(PopupAnchorCorner.Center, offset);

        /// <summary>Pinned to a region of the screen, e.g. <see cref="PopupAnchorCorner.TopRight"/> for a toast.</summary>
        public static PopupPlacement Screen(PopupAnchorCorner corner, Vector2 offset = default)
            => new(PopupPlacementMode.ScreenAnchor, corner, default, null, PopupSide.Bottom, false, null, null, offset);

        /// <summary>Centered on an explicit panel-space point.</summary>
        public static PopupPlacement ScreenPoint(Vector2 panelPoint, Vector2 offset = default)
            => new(PopupPlacementMode.ScreenPoint, PopupAnchorCorner.Center, panelPoint, null, PopupSide.Bottom, false, null, null, offset);

        /// <summary>Next to a UI element, flipping to the opposite side if it would clip off-screen.</summary>
        public static PopupPlacement AtElement(VisualElement element, PopupSide side = PopupSide.Bottom,
            bool autoFlip = true, Vector2 offset = default)
            => new(PopupPlacementMode.Element, PopupAnchorCorner.Center, default, element, side, autoFlip, null, null, offset);

        /// <summary>Above a world-space object; follows the camera and hides when behind it.</summary>
        public static PopupPlacement AtWorld(Transform anchor, Camera? camera = null, Vector2 offset = default)
            => new(PopupPlacementMode.World, PopupAnchorCorner.Center, default, null, PopupSide.Bottom, false, anchor, camera, offset);

        /// <summary>Follows the mouse cursor.</summary>
        public static PopupPlacement Cursor(Vector2 offset = default)
            => new(PopupPlacementMode.Cursor, PopupAnchorCorner.Center, default, null, PopupSide.Bottom, false, null, null, offset);
    }
}
