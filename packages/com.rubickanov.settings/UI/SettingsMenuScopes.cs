using System;
using Rubickanov.UI;

namespace Rubickanov.Settings.UI
{
    public static class SettingsMenuScopes
    {
        /// <summary>
        /// Takes the input for a settings screen in one handle: the menu keys (<see cref="SettingsMenuInput"/>), the
        /// pointer, and Back, which goes to the menu first (a question, a listening) and otherwise calls
        /// <paramref name="close"/>. Disposing the handle gives all of it back.
        /// </summary>
        /// <example><code>
        /// _scope = ui.OpenSettingsScope(menu, navigation, Close);   // Close disposes _scope, the menu, saves the store
        /// </code></example>
        public static IDisposable OpenSettingsScope(this IUIService ui, SettingsMenu menu, MenuNavigation navigation,
            Action close, MenuCue? sectionCue = null)
        {
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            if (menu == null) throw new ArgumentNullException(nameof(menu));
            if (close == null) throw new ArgumentNullException(nameof(close));

            var input = new SettingsMenuInput(menu, navigation, sectionCue);
            IDisposable scope = ui.OpenInputScope(() =>
            {
                if (!menu.Back()) close();
                return true;
            });
            return new Scope(scope, input);
        }

        private sealed class Scope : IDisposable
        {
            private IDisposable? _scope;
            private IDisposable? _input;

            public Scope(IDisposable scope, IDisposable input)
            {
                _scope = scope;
                _input = input;
            }

            public void Dispose()
            {
                IDisposable? input = _input;
                IDisposable? scope = _scope;
                _input = _scope = null;
                input?.Dispose();
                scope?.Dispose();
            }
        }
    }
}
