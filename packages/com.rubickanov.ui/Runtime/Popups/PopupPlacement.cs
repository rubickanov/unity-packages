using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI
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
        public readonly Vector3 WorldOffset;
        public readonly bool ClampToScreen;
        public readonly Camera? Camera;

        /// <summary>Panel-space offset; for a world placement, applied after projection.</summary>
        public readonly Vector2 Offset;

        private PopupPlacement(
            PopupPlacementMode mode,
            PopupAnchorCorner corner,
            Vector2 screenPointPosition,
            VisualElement? element,
            PopupSide side,
            bool autoFlip,
            Transform? worldAnchor,
            Vector3 worldOffset,
            bool clampToScreen,
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
            WorldOffset = worldOffset;
            ClampToScreen = clampToScreen;
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
            => new(PopupPlacementMode.ScreenAnchor, corner, default, null, PopupSide.Bottom, false, null, default, true, null, offset);

        /// <summary>Centered on an explicit panel-space point.</summary>
        public static PopupPlacement ScreenPoint(Vector2 panelPoint, Vector2 offset = default)
            => new(PopupPlacementMode.ScreenPoint, PopupAnchorCorner.Center, panelPoint, null, PopupSide.Bottom, false, null, default, true, null, offset);

        /// <summary>Next to a UI element, flipping to the opposite side if it would clip off-screen.</summary>
        public static PopupPlacement AtElement(VisualElement element, PopupSide side = PopupSide.Bottom,
            bool autoFlip = true, Vector2 offset = default)
            => new(PopupPlacementMode.Element, PopupAnchorCorner.Center, default, element, side, autoFlip, null, default, true, null, offset);

        /// <summary>
        /// Above a world-space object; follows it each frame and hides when it is behind the camera. The popup closes
        /// with <see cref="PopupCloseReason.AnchorDestroyed"/> once the anchor is destroyed.
        /// </summary>
        /// <param name="worldOffset">Added to the anchor's position before projection.</param>
        /// <param name="screenOffset">Panel-space offset added after projection.</param>
        /// <param name="clampToScreen">Keep the popup inside the screen. False lets a marker leave the screen.</param>
        /// <param name="camera">Projection camera. Default: the <see cref="PopupHost"/> camera provider, else <c>Camera.main</c>.</param>
        public static PopupPlacement AtWorld(Transform anchor, Vector3 worldOffset = default, Vector2 screenOffset = default,
            bool clampToScreen = true, Camera? camera = null)
            => new(PopupPlacementMode.World, PopupAnchorCorner.Center, default, null, PopupSide.Bottom, false, anchor,
                worldOffset, clampToScreen, camera, screenOffset);

        /// <summary>Follows the mouse cursor.</summary>
        public static PopupPlacement Cursor(Vector2 offset = default)
            => new(PopupPlacementMode.Cursor, PopupAnchorCorner.Center, default, null, PopupSide.Bottom, false, null, default, true, null, offset);
    }
}
