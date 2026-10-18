using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace Rubickanov.Settings
{
    /// <summary>
    /// Puts <see cref="DisplaySettings"/> on the screen at start and as they change. Nothing is switched that already is,
    /// so the first start with default settings changes nothing. The window and its size are left alone in the editor
    /// (the Game view is not the player's screen); vsync and the frame cap are not.
    /// </summary>
    public sealed class DisplayApplier : IDisposable
    {
        private readonly DisplaySettings _settings;
        private readonly IDisplayModes _modes;
        private readonly Action<string>? _log;
        private readonly CompositeDisposable _subscriptions = new();

        /// <param name="log">Each apply, what was asked and, a frame later, what the screen became.</param>
        public DisplayApplier(DisplaySettings settings, IDisplayModes modes, Action<string>? log = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _modes = modes ?? throw new ArgumentNullException(nameof(modes));
            _log = log;
        }

        /// <summary>Applies now and on each change.</summary>
        public void Start()
        {
            _subscriptions.Add(_settings.Window.Live.Subscribe(_ => ApplyScreen()));
            _subscriptions.Add(_settings.Size.Live.Subscribe(_ => ApplyScreen()));
            _subscriptions.Add(_settings.VSync.Live.Subscribe(_ => ApplyFrames()));
            _subscriptions.Add(_settings.FrameCap.Live.Subscribe(_ => ApplyFrames()));
        }

        public void Dispose() => _subscriptions.Dispose();

        /// <summary>The frame cap for a vsync and a cap setting: none while vsync paces the frames or with no cap.</summary>
        public static int TargetFrameRate(bool vsync, int cap) => vsync || cap <= 0 ? -1 : cap;

        public static FullScreenMode Mode(WindowMode window) =>
            window == WindowMode.Windowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;

        /// <summary>
        /// The size to set for a saved one. A size past the desktop (another monitor, a hand-edited file) counts as
        /// native. Native is the desktop in a full screen window; in a window it is the size it already has, or from a
        /// full screen the largest offered size under the desktop (four fifths of it when none is).
        /// </summary>
        public static ScreenSize Resolve(ScreenSize saved, FullScreenMode mode, ScreenSize desktop, ScreenSize current,
            bool windowedNow, IReadOnlyList<ScreenSize> offered)
        {
            if (!saved.IsNative && (desktop.IsNative || (saved.Width <= desktop.Width && saved.Height <= desktop.Height)))
            {
                return saved;
            }

            if (mode != FullScreenMode.Windowed || desktop.IsNative)
            {
                return desktop;
            }

            if (windowedNow && !current.IsNative && current.Width < desktop.Width && current.Height < desktop.Height)
            {
                return current;
            }

            ScreenSize best = ScreenSize.Native;
            foreach (ScreenSize size in offered)
            {
                if (size.Width < desktop.Width && size.Height < desktop.Height)
                {
                    best = size;
                }
            }

            return best.IsNative ? new ScreenSize(desktop.Width * 4 / 5, desktop.Height * 4 / 5) : best;
        }

        private void ApplyScreen()
        {
            if (Application.isEditor) return;

            FullScreenMode mode = Mode(_settings.Window.Value);
            ScreenSize size = Resolve(_settings.Size.Value, mode, _modes.Desktop, _modes.Current,
                Screen.fullScreenMode == FullScreenMode.Windowed, _modes.Sizes);
            if (Screen.fullScreenMode == mode && Screen.width == size.Width && Screen.height == size.Height) return;

            Screen.SetResolution(size.Width, size.Height, mode);
            if (_log == null) return;
            _log($"Display: asked {size} {mode} (window {_settings.Window.Text}, size {_settings.Size.Text}).");
            LogLaterAsync().Forget();
        }

        private void ApplyFrames()
        {
            int vsync = _settings.VSync.Value ? 1 : 0;
            int rate = TargetFrameRate(_settings.VSync.Value, _settings.FrameCap.Value);
            if (QualitySettings.vSyncCount == vsync && Application.targetFrameRate == rate) return;
            QualitySettings.vSyncCount = vsync;
            Application.targetFrameRate = rate;
            _log?.Invoke($"Display: vsync {vsync}, frame rate {rate}.");
        }

        private async UniTaskVoid LogLaterAsync()
        {
            await UniTask.DelayFrame(1);
            _log?.Invoke($"Display: got {Screen.width}x{Screen.height} {Screen.fullScreenMode}.");
        }
    }
}
