using UnityEngine;

namespace Rubickanov.UI
{
    /// <summary>The corner of its host a <see cref="StatPanel"/> sits in.</summary>
    public enum StatCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    /// <summary>
    /// Where a <see cref="StatPanel"/> sits, how often it reads its rows and its colours. Sizes are in the host
    /// panel's units; a font size of zero keeps the host's.
    /// </summary>
    public sealed class StatPanelOptions
    {
        public StatCorner Corner { get; set; } = StatCorner.TopLeft;

        /// <summary>From the panel to the edges of its corner.</summary>
        public float Margin { get; set; } = 10f;

        public float FontSize { get; set; }

        /// <summary>Between a row's label and its value.</summary>
        public float Gap { get; set; } = 6f;

        /// <summary>Seconds between reads of the shown rows.</summary>
        public float Interval { get; set; } = 0.25f;

        /// <summary>Labels, and values without limits.</summary>
        public Color Plain { get; set; } = new(1f, 1f, 1f, 0.6f);

        public Color Good { get; set; } = new(0.36f, 0.89f, 0.48f, 0.9f);

        public Color Warn { get; set; } = new(1f, 0.82f, 0.25f, 0.9f);

        public Color Bad { get; set; } = new(1f, 0.29f, 0.24f, 0.9f);

        /// <summary>Under every letter, so the text reads on a bright picture too; clear for none.</summary>
        public Color Shadow { get; set; } = new(0f, 0f, 0f, 0.7f);

        /// <summary>How far the shadow falls, down and to the right.</summary>
        public float ShadowOffset { get; set; } = 1f;

        public Color Colour(StatLevel level) => level switch
        {
            StatLevel.Good => Good,
            StatLevel.Warn => Warn,
            StatLevel.Bad => Bad,
            _ => Plain,
        };
    }
}
