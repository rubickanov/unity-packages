using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// Pure positioning math: turns a <see cref="PopupPlacement"/> plus a measured panel size into a
    /// panel-space top-left for the popup panel.
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
        /// <param name="camera">Camera for a world placement without its own camera.</param>
        /// <param name="topLeft">Resolved top-left in panel space.</param>
        /// <param name="resolvedSide">Side actually used after auto-flip (element placement).</param>
        /// <returns>False when the popup should be hidden (e.g. a world anchor behind the camera).</returns>
        public static bool TryResolve(VisualElement layer, in PopupPlacement p, Vector2 size,
            Vector2 cursorPanelPos, Camera? camera, out Vector2 topLeft, out PopupSide resolvedSide)
        {
            var worldPanelPoint = Vector2.zero;
            if (p.Mode == PopupPlacementMode.World)
            {
                if (p.WorldAnchor == null) return Hidden(p, out topLeft, out resolvedSide);
                var cam = p.Camera != null ? p.Camera : camera;
                if (cam == null) return Hidden(p, out topLeft, out resolvedSide);
                var screen = cam.WorldToScreenPoint(p.WorldAnchor.position + p.WorldOffset);
                if (screen.z < 0f) return Hidden(p, out topLeft, out resolvedSide);
                worldPanelPoint = ScreenToPanel(layer, new Vector2(screen.x, screen.y));
            }

            var layerSize = new Vector2(layer.resolvedStyle.width, layer.resolvedStyle.height);
            return TryResolve(p, size, layerSize, cursorPanelPos, worldPanelPoint, out topLeft, out resolvedSide);
        }

        /// <summary>
        /// Placement math with every input given: <paramref name="layerSize"/> bounds the popup, and
        /// <paramref name="worldPanelPoint"/> is the projected world anchor in panel space (world placement only).
        /// </summary>
        public static bool TryResolve(in PopupPlacement p, Vector2 size, Vector2 layerSize, Vector2 cursorPanelPos,
            Vector2 worldPanelPoint, out Vector2 topLeft, out PopupSide resolvedSide)
        {
            resolvedSide = p.Side;

            var panelW = layerSize.x;
            var panelH = layerSize.y;
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
                    // Hover above the object, like a speech bubble.
                    topLeft = new Vector2(worldPanelPoint.x - w * 0.5f, worldPanelPoint.y - h) + p.Offset;
                    break;

                case PopupPlacementMode.Cursor:
                    topLeft = cursorPanelPos + p.Offset;
                    break;

                default:
                    topLeft = Vector2.zero;
                    break;
            }

            var clamp = p.Mode != PopupPlacementMode.World || p.ClampToScreen;
            if (hasSize && clamp)
                topLeft = Clamp(topLeft, w, h, panelW, panelH);

            return true;
        }

        private static bool Hidden(in PopupPlacement p, out Vector2 topLeft, out PopupSide resolvedSide)
        {
            topLeft = Vector2.zero;
            resolvedSide = p.Side;
            return false;
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
            var panelSize = layer.panel != null ? layer.panel.visualTree.worldBound.size : Vector2.zero;
            return ScreenToPanel(screen, new Vector2(Screen.width, Screen.height), panelSize);
        }

        /// <summary>Screen pixels (bottom-left origin) to a panel of <paramref name="panelSize"/> (top-left origin).</summary>
        public static Vector2 ScreenToPanel(Vector2 screen, Vector2 screenSize, Vector2 panelSize)
        {
            if (panelSize.x <= 0f || panelSize.y <= 0f || screenSize.x <= 0f || screenSize.y <= 0f)
                return new Vector2(screen.x, screenSize.y - screen.y);

            return new Vector2(screen.x * panelSize.x / screenSize.x,
                               (screenSize.y - screen.y) * panelSize.y / screenSize.y);
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
