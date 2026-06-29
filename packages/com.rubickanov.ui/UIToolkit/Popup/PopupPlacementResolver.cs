using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    /// <summary>
    /// Pure positioning math: turns a <see cref="PopupPlacement"/> plus a measured panel size into a
    /// panel-space top-left for the popup panel. Generalizes the logic in TooltipService and the
    /// world-anchored speech bubble in the game's CustomerWorldView.
    /// </summary>
    internal static class PopupPlacementResolver
    {
        /// <summary>
        /// Resolves the panel-space top-left corner for a popup panel.
        /// </summary>
        /// <param name="layer">The layer container the popup lives in (provides panel + bounds).</param>
        /// <param name="p">The placement description.</param>
        /// <param name="size">Measured panel size; may contain NaN before the first layout pass.</param>
        /// <param name="cursorPanelPos">Latest pointer position in panel space (for cursor placement).</param>
        /// <param name="topLeft">Resolved top-left in panel space.</param>
        /// <param name="resolvedSide">Side actually used after auto-flip (element placement).</param>
        /// <returns>False when the popup should be hidden (e.g. a world anchor behind the camera).</returns>
        public static bool TryResolve(VisualElement layer, in PopupPlacement p, Vector2 size,
            Vector2 cursorPanelPos, out Vector2 topLeft, out PopupSide resolvedSide)
        {
            resolvedSide = p.Side;

            var panelW = layer.resolvedStyle.width;
            var panelH = layer.resolvedStyle.height;
            var w = size.x;
            var h = size.y;
            var hasSize = !float.IsNaN(w) && !float.IsNaN(h) && !float.IsNaN(panelW) && !float.IsNaN(panelH);

            switch (p.Mode)
            {
                case PopupPlacementMode.ScreenAnchor:
                    topLeft = ResolveScreenAnchor(p.Corner, p.Offset, w, h, panelW, panelH);
                    break;

                case PopupPlacementMode.ScreenPoint:
                    topLeft = new Vector2(p.ScreenPointPosition.x - w * 0.5f,
                                          p.ScreenPointPosition.y - h * 0.5f) + p.Offset;
                    break;

                case PopupPlacementMode.Element:
                    if (p.Element == null) { topLeft = Vector2.zero; return false; }
                    topLeft = ResolveElement(p.Element.worldBound, p.Side, p.Offset, w, h);
                    if (p.AutoFlip && hasSize)
                        topLeft = ApplyFlip(p.Element.worldBound, ref resolvedSide, p.Offset, w, h,
                            panelW, panelH, topLeft);
                    break;

                case PopupPlacementMode.World:
                    if (p.WorldAnchor == null) { topLeft = Vector2.zero; return false; }
                    var cam = p.Camera != null ? p.Camera : Camera.main;
                    if (cam == null) { topLeft = Vector2.zero; return false; }
                    var screen = cam.WorldToScreenPoint(p.WorldAnchor.position);
                    if (screen.z < 0f) { topLeft = Vector2.zero; return false; }
                    var panelPos = ScreenToPanel(layer, new Vector2(screen.x, screen.y));
                    // Hover above the object, like a speech bubble.
                    topLeft = new Vector2(panelPos.x - w * 0.5f, panelPos.y - h) + p.Offset;
                    break;

                case PopupPlacementMode.Cursor:
                    topLeft = cursorPanelPos + p.Offset;
                    break;

                default:
                    topLeft = Vector2.zero;
                    break;
            }

            if (hasSize)
                topLeft = Clamp(topLeft, w, h, panelW, panelH);

            return true;
        }

        /// <summary>
        /// Converts a screen-pixel position (bottom-left origin, as from the camera or input device)
        /// to panel space (top-left origin), assuming the panel scales to fill the screen — the case
        /// for runtime overlay UIDocuments. Done manually rather than via
        /// <c>RuntimePanelUtils.ScreenToPanel</c> because that does not flip Y consistently with the
        /// panel coordinate system across the scale modes used here, producing a mirrored result.
        /// </summary>
        public static Vector2 ScreenToPanel(VisualElement layer, Vector2 screen)
        {
            var size = layer.panel.visualTree.worldBound.size;
            if (size.x <= 0f || size.y <= 0f)
                return new Vector2(screen.x, Screen.height - screen.y);

            var scaleX = Screen.width / size.x;
            var scaleY = Screen.height / size.y;
            return new Vector2(screen.x / scaleX, (Screen.height - screen.y) / scaleY);
        }

        private static Vector2 ResolveScreenAnchor(PopupAnchorCorner corner, Vector2 offset,
            float w, float h, float panelW, float panelH)
        {
            float x = corner switch
            {
                PopupAnchorCorner.TopLeft or PopupAnchorCorner.Left or PopupAnchorCorner.BottomLeft => offset.x,
                PopupAnchorCorner.TopRight or PopupAnchorCorner.Right or PopupAnchorCorner.BottomRight => panelW - w - offset.x,
                _ => (panelW - w) * 0.5f
            };
            float y = corner switch
            {
                PopupAnchorCorner.TopLeft or PopupAnchorCorner.Top or PopupAnchorCorner.TopRight => offset.y,
                PopupAnchorCorner.BottomLeft or PopupAnchorCorner.Bottom or PopupAnchorCorner.BottomRight => panelH - h - offset.y,
                _ => (panelH - h) * 0.5f
            };
            return new Vector2(x, y);
        }

        private static Vector2 ResolveElement(Rect b, PopupSide side, Vector2 offset, float w, float h)
        {
            return side switch
            {
                PopupSide.Top => new Vector2(b.center.x - w * 0.5f, b.yMin - h - offset.y),
                PopupSide.Bottom => new Vector2(b.center.x - w * 0.5f, b.yMax + offset.y),
                PopupSide.Left => new Vector2(b.xMin - w - offset.x, b.center.y - h * 0.5f),
                PopupSide.Right => new Vector2(b.xMax + offset.x, b.center.y - h * 0.5f),
                _ => new Vector2(b.center.x - w * 0.5f, b.yMax + offset.y)
            };
        }

        private static Vector2 ApplyFlip(Rect b, ref PopupSide side, Vector2 offset, float w, float h,
            float panelW, float panelH, Vector2 current)
        {
            switch (side)
            {
                case PopupSide.Bottom when current.y + h > panelH:
                    side = PopupSide.Top;
                    return ResolveElement(b, side, offset, w, h);
                case PopupSide.Top when current.y < 0f:
                    side = PopupSide.Bottom;
                    return ResolveElement(b, side, offset, w, h);
                case PopupSide.Right when current.x + w > panelW:
                    side = PopupSide.Left;
                    return ResolveElement(b, side, offset, w, h);
                case PopupSide.Left when current.x < 0f:
                    side = PopupSide.Right;
                    return ResolveElement(b, side, offset, w, h);
                default:
                    return current;
            }
        }

        private static Vector2 Clamp(Vector2 topLeft, float w, float h, float panelW, float panelH)
        {
            var x = topLeft.x;
            var y = topLeft.y;
            if (x + w > panelW) x = panelW - w;
            if (x < 0f) x = 0f;
            if (y + h > panelH) y = panelH - h;
            if (y < 0f) y = 0f;
            return new Vector2(x, y);
        }
    }
}
