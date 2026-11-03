using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Settings
{
    /// <summary>What the screen can be set to, apart from where the numbers come from (tests give their own).</summary>
    public interface IDisplayModes
    {
        /// <summary>The sizes the screen offers, smallest first, each once.</summary>
        IReadOnlyList<ScreenSize> Sizes { get; }

        /// <summary>The size the game shows now.</summary>
        ScreenSize Current { get; }

        /// <summary>The desktop's size.</summary>
        ScreenSize Desktop { get; }
    }

    /// <summary>The screen's own sizes (<see cref="Screen.resolutions"/>) without the repeats for each refresh rate.</summary>
    public sealed class ScreenModes : IDisplayModes
    {
        public IReadOnlyList<ScreenSize> Sizes
        {
            get
            {
                Resolution[] resolutions = Screen.resolutions;
                var sizes = new ScreenSize[resolutions.Length];
                for (int i = 0; i < resolutions.Length; i++) sizes[i] = new ScreenSize(resolutions[i].width, resolutions[i].height);
                return Distinct(sizes);
            }
        }

        public ScreenSize Current => new(Screen.width, Screen.height);

        public ScreenSize Desktop => new(Screen.currentResolution.width, Screen.currentResolution.height);

        /// <summary>Each size once, without native, smallest area first (then narrowest).</summary>
        public static IReadOnlyList<ScreenSize> Distinct(IEnumerable<ScreenSize> sizes)
        {
            var list = new List<ScreenSize>();
            foreach (ScreenSize size in sizes)
            {
                if (!size.IsNative && !list.Contains(size)) list.Add(size);
            }

            list.Sort((a, b) =>
            {
                int area = (a.Width * a.Height).CompareTo(b.Width * b.Height);
                return area != 0 ? area : a.Width.CompareTo(b.Width);
            });
            return list;
        }
    }
}
