using System;
using System.Collections.Generic;
using System.Globalization;

namespace Rubickanov.Settings
{
    /// <summary>
    /// The ready-made display settings: window or full screen, size, vsync, frame cap. Ids are <c>prefix.window</c>,
    /// <c>prefix.size</c>, <c>prefix.vsync</c>, <c>prefix.frameCap</c>. <see cref="DisplayApplier"/> puts them on the screen.
    /// </summary>
    public sealed class DisplaySettings
    {
        /// <summary>The frame caps offered; 0 is none.</summary>
        public static readonly int[] FrameCaps = { 30, 60, 120, 144, 0 };

        private static readonly WindowMode[] Windows = { WindowMode.Fullscreen, WindowMode.Windowed };

        public DisplaySettings(SettingsStore store, string prefix = "display", WindowMode window = WindowMode.Fullscreen,
            bool vsync = true, int frameCap = 60)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            Prefix = prefix;
            Window = store.Enum(prefix + ".window", window);
            Size = store.Add(new Setting<ScreenSize>(prefix + ".size", ScreenSize.Native, SettingFormats.ScreenSize));
            VSync = store.Toggle(prefix + ".vsync", vsync);
            FrameCap = store.Choice(prefix + ".frameCap", frameCap, FrameCaps, SettingFormats.Int);
        }

        public string Prefix { get; }

        public Setting<WindowMode> Window { get; }

        /// <summary>The window's or the full screen's size; <see cref="ScreenSize.Native"/> is the screen's own.</summary>
        public Setting<ScreenSize> Size { get; }

        public Setting<bool> VSync { get; }

        public ChoiceSetting<int> FrameCap { get; }

        /// <summary>
        /// Window and size (both keep-or-revert: picked freely, put on by APPLY under them and kept or reverted then;
        /// the sizes read when the section opens, native shown as the size it is now), vsync, and the frame cap (off
        /// while vsync is on).
        /// </summary>
        public void AddRows(SectionBuilder section, IDisplayModes modes)
        {
            if (modes == null) throw new ArgumentNullException(nameof(modes));
            string windowKey = Window.Id + "/";
            section.Choice(Window.Id, Window, Windows, mode => windowKey + SettingFormats.Enum<WindowMode>().Show(mode))
                .Resets(Window)
                .KeepOrRevert();

            var shown = new ShownSize(Size, modes);
            section.Choice(Size.Id, shown, () => Offered(modes), (size, text) => Show(size, text))
                .Resets(Size)
                .KeepOrRevert();
            section.ApplyRow();

            section.Toggle(VSync);
            section.Choice(FrameCap.Id, FrameCap, FrameCaps, (cap, text) => ShowCap(cap, text))
                .Enabled(() => !VSync.Value);
        }

        // Native shown as the size it is now; a revert puts native back, not that size.
        private sealed class ShownSize : ISettingValue<ScreenSize>, IRemembered
        {
            private readonly Setting<ScreenSize> _size;
            private readonly IDisplayModes _modes;

            public ShownSize(Setting<ScreenSize> size, IDisplayModes modes)
            {
                _size = size;
                _modes = modes;
            }

            public ScreenSize Value
            {
                get => _size.Value.IsNative ? _modes.Current : _size.Value;
                set => _size.Value = value;
            }

            public Action Remember()
            {
                ScreenSize was = _size.Value;
                return () => _size.Value = was;
            }
        }

        private static IReadOnlyList<ScreenSize> Offered(IDisplayModes modes)
        {
            IReadOnlyList<ScreenSize> sizes = modes.Sizes;
            if (sizes.Count > 0) return sizes;
            return modes.Current.IsNative ? new[] { ScreenSize.Native } : new[] { modes.Current };
        }

        private static string Show(ScreenSize size, ISettingsText text) => size.IsNative
            ? text.Get("settings.native")
            : size.Width.ToString(CultureInfo.InvariantCulture) + " × " + size.Height.ToString(CultureInfo.InvariantCulture);

        private static string ShowCap(int cap, ISettingsText text) =>
            cap > 0 ? cap.ToString(CultureInfo.InvariantCulture) : text.Get("settings.nocap");
    }
}
