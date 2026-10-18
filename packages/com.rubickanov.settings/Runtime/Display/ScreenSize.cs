using System;
using System.Globalization;

namespace Rubickanov.Settings
{
    /// <summary>A screen size in pixels; 0 × 0 is the screen's own (<see cref="Native"/>).</summary>
    public readonly struct ScreenSize : IEquatable<ScreenSize>
    {
        public const string NativeText = "native";

        public ScreenSize(int width, int height)
        {
            Width = Math.Max(0, width);
            Height = Math.Max(0, height);
        }

        public static ScreenSize Native => default;

        public int Width { get; }

        public int Height { get; }

        public bool IsNative => Width == 0 || Height == 0;

        public bool Equals(ScreenSize other) => IsNative ? other.IsNative : Width == other.Width && Height == other.Height;

        public override bool Equals(object obj) => obj is ScreenSize other && Equals(other);

        public override int GetHashCode() => IsNative ? 0 : (Width * 397) ^ Height;

        public static bool operator ==(ScreenSize a, ScreenSize b) => a.Equals(b);

        public static bool operator !=(ScreenSize a, ScreenSize b) => !a.Equals(b);

        /// <summary><c>1920x1080</c>, <c>native</c>.</summary>
        public override string ToString() => IsNative
            ? NativeText
            : Width.ToString(CultureInfo.InvariantCulture) + "x" + Height.ToString(CultureInfo.InvariantCulture);

        /// <summary>Reads <c>1920x1080</c> (also <c>1920 × 1080</c>) and <c>native</c>.</summary>
        public static bool TryParse(string text, out ScreenSize size)
        {
            size = Native;
            if (text == null) return false;
            text = text.Trim();
            if (string.Equals(text, NativeText, StringComparison.OrdinalIgnoreCase)) return true;
            int cross = text.IndexOfAny(new[] { 'x', 'X', '×' });
            if (cross <= 0) return false;
            if (!int.TryParse(text.Substring(0, cross).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int width) ||
                !int.TryParse(text.Substring(cross + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int height))
            {
                return false;
            }

            size = new ScreenSize(width, height);
            return true;
        }
    }

    public enum WindowMode
    {
        /// <summary>A borderless window over the whole screen (<c>FullScreenMode.FullScreenWindow</c>).</summary>
        Fullscreen,
        Windowed,
    }
}
