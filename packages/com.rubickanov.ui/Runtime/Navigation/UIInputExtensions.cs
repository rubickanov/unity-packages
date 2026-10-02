using System;

namespace Rubickanov.UI
{
    public static class UIInputExtensions
    {
        /// <summary>
        /// Routes a back press, a pause press or both (Escape is both, a pad's B is only back, its Start only pause) the
        /// way games do: to <see cref="IUIService.Back"/> first, so a popup closes and then a screen goes back; when
        /// nothing takes it, a pause press calls <paramref name="openPause"/>. True when either took it.
        /// </summary>
        /// <example><code>
        /// ui.BackOrPause(input.Cancel.WasPressedThisFrame(), input.Pause.WasPressedThisFrame(), OpenPauseMenu);
        /// </code></example>
        public static bool BackOrPause(this IUIService ui, bool back, bool pause, Action openPause)
        {
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            if (openPause == null) throw new ArgumentNullException(nameof(openPause));
            if (!back && !pause) return false;
            if (ui.Back()) return true;
            if (!pause) return false;

            openPause();
            return true;
        }

        /// <summary>
        /// Takes the input for something the UI service does not show itself (a panel of the game's own, a view model
        /// asking a question): a back handler, a pointer capture, and the menu keys for <paramref name="menu"/> when
        /// given. Disposing the handle gives all of it back. With no <paramref name="back"/>, Back stops here unanswered,
        /// as at a modal popup.
        /// </summary>
        public static IDisposable OpenInputScope(this IUIService ui, Func<bool>? back = null,
            MenuNavigation? navigation = null, IMenuTarget? menu = null)
        {
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            if (menu != null && navigation == null)
                throw new ArgumentNullException(nameof(navigation), "A menu needs the navigation that feeds it.");

            var backHandle = ui.PushBackHandler(back ?? Swallow);
            var capture = ui.CapturePointer();
            var claim = menu != null ? navigation!.Claim(menu) : null;
            return new InputScope(backHandle, capture, claim);
        }

        private static bool Swallow() => true;

        private sealed class InputScope : IDisposable
        {
            private IDisposable? _back;
            private IDisposable? _capture;
            private IDisposable? _claim;

            public InputScope(IDisposable back, IDisposable capture, IDisposable? claim)
            {
                _back = back;
                _capture = capture;
                _claim = claim;
            }

            public void Dispose()
            {
                var back = _back;
                var capture = _capture;
                var claim = _claim;
                _back = _capture = _claim = null;
                claim?.Dispose();
                capture?.Dispose();
                back?.Dispose();
            }
        }
    }
}
