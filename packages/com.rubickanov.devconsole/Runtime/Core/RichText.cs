using System.Text.RegularExpressions;

namespace Rubickanov.DevConsole
{
    /// <summary>Removes Unity rich text tags from console lines.</summary>
    internal static class RichText
    {
        // Only tags Unity knows, so text like List<int> or <none> stays as it is.
        private static readonly Regex AnyTag = new(
            @"</?(?:b|i|u|s|size|color|alpha|material|quad|mark|sup|sub|font|voffset|cspace|mspace|indent|" +
            @"line-height|align|lowercase|uppercase|allcaps|smallcaps|noparse|nobr|sprite|link|style|pos|margin|" +
            @"width|rotate)(?:[= ][^<>]*)?>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Every tag above except the ones that only change colour and never move a glyph.
        private static readonly Regex LayoutTag = new(
            @"</?(?:b|i|u|s|size|material|quad|mark|sup|sub|font|voffset|cspace|mspace|indent|" +
            @"line-height|align|lowercase|uppercase|allcaps|smallcaps|noparse|nobr|sprite|link|style|pos|margin|" +
            @"width|rotate)(?:[= ][^<>]*)?>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The text as the reader sees it, without any tags.</summary>
        public static string Strip(string text) =>
            string.IsNullOrEmpty(text) || text.IndexOf('<') < 0 ? text ?? "" : AnyTag.Replace(text, "");

        /// <summary>
        /// The text with colour tags only. It is laid out glyph for glyph like <see cref="Strip"/>, so positions
        /// measured on the plain text hold for the coloured one.
        /// </summary>
        public static string KeepColor(string text) =>
            string.IsNullOrEmpty(text) || text.IndexOf('<') < 0 ? text ?? "" : LayoutTag.Replace(text, "");
    }
}
