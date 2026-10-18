using System;
using System.Collections.Generic;

namespace Rubickanov.Settings
{
    /// <summary>The settings screen's sections in order, each a list of rows the game declares.</summary>
    public sealed class SettingsLayout
    {
        private readonly List<SettingsSection> _sections = new();

        public IReadOnlyList<SettingsSection> Sections => _sections;

        /// <param name="onReset">Also done by the section's reset (keys of an input package, a file of the game's).</param>
        public SettingsLayout Section(string id, Action<SectionBuilder> rows, Action? onReset = null)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var section = new SettingsSection(id, onReset);
            rows(new SectionBuilder(section));
            _sections.Add(section);
            return this;
        }
    }

    /// <summary>One section: its id (title key <c>section.id</c>), its rows and providers of rows, its own reset.</summary>
    public sealed class SettingsSection
    {
        private readonly List<object> _entries = new();

        internal SettingsSection(string id, Action? onReset)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            OnReset = onReset;
        }

        public string Id { get; }

        internal Action? OnReset { get; }

        internal void Add(SettingsRow row) => _entries.Add(row);

        internal void Add(Func<IEnumerable<SettingsRow>> rows) => _entries.Add(rows);

        /// <summary>Every row of the section, providers asked again.</summary>
        internal void MakeRows(List<SettingsRow> rows)
        {
            rows.Clear();
            foreach (object entry in _entries)
            {
                if (entry is SettingsRow row)
                {
                    rows.Add(row);
                }
                else if (entry is Func<IEnumerable<SettingsRow>> provider)
                {
                    foreach (SettingsRow made in provider()) rows.Add(made);
                }
            }
        }
    }

    /// <summary>Declares a section's rows, in order.</summary>
    public sealed class SectionBuilder
    {
        private readonly SettingsSection _section;

        internal SectionBuilder(SettingsSection section) => _section = section;

        public string SectionId => _section.Id;

        public StepsRow Steps(StepSetting setting) => Add(new StepsRow(setting.Id, setting, setting.Min, setting.Max));

        public StepsRow Steps(string id, ISettingValue<int> value, int min, int max) => Add(new StepsRow(id, value, min, max));

        public ToggleRow Toggle(Setting<bool> setting) => Add(new ToggleRow(setting.Id, setting));

        public ToggleRow Toggle(string id, ISettingValue<bool> value) => Add(new ToggleRow(id, value));

        /// <param name="optionKey">An option's text key: <c>o => "display.window/" + o</c>.</param>
        public ChoiceRow<T> Choice<T>(ChoiceSetting<T> setting, Func<T, string> optionKey) =>
            Add(new ChoiceRow<T>(setting.Id, setting, setting.Options, Keyed(optionKey)));

        public ChoiceRow<T> Choice<T>(string id, ISettingValue<T> value, IReadOnlyList<T> options, Func<T, string> optionKey) =>
            Add(new ChoiceRow<T>(id, value, options, Keyed(optionKey)));

        /// <param name="show">An option's text, from the table when it needs one (<c>settings.nocap</c>).</param>
        public ChoiceRow<T> Choice<T>(string id, ISettingValue<T> value, IReadOnlyList<T> options,
            Func<T, ISettingsText, string> show) => Add(new ChoiceRow<T>(id, value, options, show));

        /// <summary>Options read each time the section opens (screen sizes, microphones), shown as they are.</summary>
        public ChoiceRow<T> Choice<T>(string id, ISettingValue<T> value, Func<IReadOnlyList<T>> liveOptions,
            Func<T, string> show) => Add(new ChoiceRow<T>(id, value, liveOptions, (option, _) => show(option)));

        public ChoiceRow<T> Choice<T>(string id, ISettingValue<T> value, Func<IReadOnlyList<T>> liveOptions,
            Func<T, ISettingsText, string> show) => Add(new ChoiceRow<T>(id, value, liveOptions, show));

        public ActionRow Action(string id, Action act) => Add(new ActionRow(id, act));

        public HeadingRow Heading(string id) => Add(new HeadingRow(id));

        /// <summary>A row of a kind of the game's own.</summary>
        public TRow Add<TRow>(TRow row) where TRow : SettingsRow
        {
            _section.Add(row ?? throw new ArgumentNullException(nameof(row)));
            return row;
        }

        /// <summary>Rows made again each time the section opens or is rebuilt (keys of the device in hand).</summary>
        public void Rows(Func<IEnumerable<SettingsRow>> rows) => _section.Add(rows ?? throw new ArgumentNullException(nameof(rows)));

        /// <summary><c>settings.reset</c>: asks first, then puts every row of the section back, and the section's own reset.</summary>
        public ActionRow ResetRow() => Add(new ActionRow(ResetId, menu => (menu as SettingsMenu)?.AskReset()));

        public const string ResetId = "settings.reset";

        private static Func<T, ISettingsText, string> Keyed<T>(Func<T, string> optionKey)
        {
            if (optionKey == null) throw new ArgumentNullException(nameof(optionKey));
            return (option, text) => text.Get(optionKey(option));
        }
    }
}
