using System;
using R3;
using Rubickanov.UI;

namespace Rubickanov.Settings.UI
{
    /// <summary>
    /// A <see cref="SettingsMenu"/> on the ui package's keys: claims <see cref="MenuNavigation"/> while it lives (so the
    /// pause or title under it hears no steps), forwards steps and presses, and sends the menu's cues as
    /// <see cref="MenuCue"/>s.
    /// </summary>
    public sealed class SettingsMenuInput : IMenuTarget, IDisposable
    {
        /// <summary>A value changed.</summary>
        public static readonly MenuCue Change = new("settings-change");

        private readonly SettingsMenu _menu;
        private readonly MenuNavigation _navigation;
        private readonly MenuCue _sectionCue;
        private IDisposable? _claim;
        private IDisposable? _cues;

        /// <param name="sectionCue">The cue of another section; <see cref="MenuCue.Focus"/> when null.</param>
        public SettingsMenuInput(SettingsMenu menu, MenuNavigation navigation, MenuCue? sectionCue = null)
        {
            _menu = menu ?? throw new ArgumentNullException(nameof(menu));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _sectionCue = sectionCue ?? MenuCue.Focus;
            _cues = menu.Cues.Subscribe(cue => _navigation.Cue(Of(cue)));
            _claim = navigation.Claim(this);
        }

        public void Step(MenuStep step)
        {
            switch (step)
            {
                case MenuStep.Up:
                    _menu.Step(SettingsStep.Up);
                    break;
                case MenuStep.Down:
                    _menu.Step(SettingsStep.Down);
                    break;
                case MenuStep.Left:
                    _menu.Step(SettingsStep.Left);
                    break;
                case MenuStep.Right:
                    _menu.Step(SettingsStep.Right);
                    break;
                case MenuStep.PreviousTab:
                    _menu.Step(SettingsStep.PreviousSection);
                    break;
                case MenuStep.NextTab:
                    _menu.Step(SettingsStep.NextSection);
                    break;
            }
        }

        public void Submit() => _menu.Submit();

        public void Dispose()
        {
            _claim?.Dispose();
            _cues?.Dispose();
            _claim = _cues = null;
        }

        private MenuCue Of(SettingsCue cue) => cue switch
        {
            SettingsCue.Change => Change,
            SettingsCue.Select => MenuCue.Select,
            SettingsCue.Back => MenuCue.Back,
            SettingsCue.Denied => MenuCue.Denied,
            SettingsCue.Section => _sectionCue,
            _ => MenuCue.Focus,
        };
    }
}
