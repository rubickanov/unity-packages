using UnityEngine;

namespace Rubickanov.UI
{
    /// <summary>
    /// Where a popup's panel sits on its layer. Laid out by flex alignment, not measured: a panel that changes size
    /// stays in place without a layout pass of its own.
    /// </summary>
    public readonly struct PopupPlacement
    {
        /// <summary>The panel is stretched over the whole layer.</summary>
        public readonly bool IsFill;

        public readonly PopupAnchorCorner Corner;

        /// <summary>
        /// Panel-space offset: in from the edges the panel is pinned to, along an axis it is centred on.
        /// </summary>
        public readonly Vector2 Offset;

        private PopupPlacement(bool isFill, PopupAnchorCorner corner, Vector2 offset)
        {
            IsFill = isFill;
            Corner = corner;
            Offset = offset;
        }

        /// <summary>Covers the whole layer, like a screen. What a popup view (<c>ShowView</c>) uses.</summary>
        public static PopupPlacement Fill() => new(true, PopupAnchorCorner.Center, default);

        /// <summary>Centered on the layer, moved by <paramref name="offset"/>.</summary>
        public static PopupPlacement ScreenCenter(Vector2 offset = default) => Screen(PopupAnchorCorner.Center, offset);

        /// <summary>Pinned to a region of the layer, <paramref name="offset"/> in from its edges: a toast at the top right.</summary>
        public static PopupPlacement Screen(PopupAnchorCorner corner, Vector2 offset = default) => new(false, corner, offset);
    }
}
