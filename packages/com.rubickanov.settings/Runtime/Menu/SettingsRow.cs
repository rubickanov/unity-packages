using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Settings
{
    /// <summary>
    /// One row of a settings section. A row reads its value each time it is asked, so a change made by the console or
    /// code shows at once. A game's own kind of row overrides <see cref="Step"/> and <see cref="Submit"/> (and
    /// <see cref="Current"/> to take part in keep-or-revert and the change cue) and draws itself.
    /// </summary>
    public abstract class SettingsRow
    {
        private Func<bool>? _visible;
        private Func<bool>? _enabled;
        private List<IResettable>? _resets;

        protected SettingsRow(string id)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
        }

        /// <summary>The row's id: its label's text key, and how the focus finds it again.</summary>
        public string Id { get; }

        public bool IsVisible => _visible?.Invoke() ?? true;

        public bool IsEnabled => _enabled?.Invoke() ?? true;

        /// <summary>A change asks "keep?" and goes back by itself when nobody answers (window, resolution).</summary>
        public bool IsKeepOrRevert { get; private set; }

        public virtual bool Focusable => IsEnabled;

        /// <summary>What the section's reset puts back.</summary>
        public virtual IEnumerable<IResettable> Resettables =>
            _resets ?? (IEnumerable<IResettable>)Array.Empty<IResettable>();

        /// <summary>Shown only while <paramref name="when"/> holds; asked again on <see cref="SettingsMenu.Refresh"/>.</summary>
        public SettingsRow Visible(Func<bool> when)
        {
            _visible = when;
            return this;
        }

        /// <summary>Changeable only while <paramref name="when"/> holds.</summary>
        public SettingsRow Enabled(Func<bool> when)
        {
            _enabled = when;
            return this;
        }

        public SettingsRow KeepOrRevert()
        {
            IsKeepOrRevert = true;
            return this;
        }

        /// <summary>Adds what the section's reset puts back for this row.</summary>
        public SettingsRow Resets(params IResettable[] values)
        {
            _resets ??= new List<IResettable>();
            foreach (IResettable value in values)
            {
                if (value != null && !_resets.Contains(value)) _resets.Add(value);
            }

            return this;
        }

        /// <summary>The value shown, compared before and after a step to tell a change; null for rows without one.</summary>
        protected internal virtual object? Current => null;

        /// <summary>What puts the value shown now back (keep-or-revert); null for rows without one.</summary>
        protected internal virtual Action? Remember() => null;

        /// <summary>Left (-1) or right (+1); true when what the row shows changed.</summary>
        protected internal virtual bool Step(int direction, ISettingsMenu menu) => false;

        /// <summary>Enter or a click; true when the row did something.</summary>
        protected internal virtual bool Submit(ISettingsMenu menu) => false;

        /// <summary>The section was opened or rebuilt: options read live are read now.</summary>
        protected internal virtual void Open()
        {
        }

        /// <summary>The line under the rows while this one has the focus.</summary>
        protected internal virtual string? About(ISettingsText text) => text.TryGet(Id + ".about", out string about) ? about : null;

        private protected void ResetsValue(object value)
        {
            if (value is IResettable resettable) Resets(resettable);
        }

        private protected static Action RememberOf<T>(ISettingValue<T> value)
        {
            if (value is IRemembered remembered) return remembered.Remember();
            T was = value.Value;
            return () => value.Value = was;
        }
    }

    /// <summary>A value shown as something else (native as the size it is now) that puts back what it really was.</summary>
    internal interface IRemembered
    {
        Action Remember();
    }

    /// <summary>Whole steps with an end each way (a meter).</summary>
    public sealed class StepsRow : SettingsRow
    {
        private readonly ISettingValue<int> _value;

        public StepsRow(string id, ISettingValue<int> value, int min, int max) : base(id)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
            Min = min;
            Max = Math.Max(min, max);
            ResetsValue(value);
        }

        public int Value => _value.Value;

        public int Min { get; }

        public int Max { get; }

        protected internal override object? Current => _value.Value;

        protected internal override Action? Remember() => RememberOf(_value);

        protected internal override bool Step(int direction, ISettingsMenu menu)
        {
            int next = Math.Clamp(_value.Value + direction, Min, Max);
            if (next == _value.Value) return false;
            _value.Value = next;
            return true;
        }
    }

    /// <summary>On or off: left is on, right off, Enter flips.</summary>
    public sealed class ToggleRow : SettingsRow
    {
        private readonly ISettingValue<bool> _value;

        public ToggleRow(string id, ISettingValue<bool> value) : base(id)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
            ResetsValue(value);
        }

        public bool Value => _value.Value;

        protected internal override object? Current => _value.Value;

        protected internal override Action? Remember() => RememberOf(_value);

        protected internal override bool Step(int direction, ISettingsMenu menu)
        {
            bool on = direction < 0;
            if (on == _value.Value) return false;
            _value.Value = on;
            return true;
        }

        protected internal override bool Submit(ISettingsMenu menu)
        {
            _value.Value = !_value.Value;
            return true;
        }
    }

    /// <summary>One of a list: left and right with an end each way, Enter goes round. Untyped, for drawing.</summary>
    public abstract class ChoiceRow : SettingsRow
    {
        protected ChoiceRow(string id) : base(id)
        {
        }

        public abstract int Count { get; }

        /// <summary>The shown option; -1 with none.</summary>
        public abstract int Index { get; }

        public abstract string OptionText(int option, ISettingsText text);

        /// <summary>Every option's text, in order (a new list).</summary>
        public IReadOnlyList<string> OptionTexts(ISettingsText text)
        {
            var texts = new string[Count];
            for (int i = 0; i < texts.Length; i++) texts[i] = OptionText(i, text);
            return texts;
        }
    }

    public sealed class ChoiceRow<T> : ChoiceRow
    {
        private readonly ISettingValue<T> _value;
        private readonly Func<IReadOnlyList<T>>? _liveOptions;
        private readonly Func<T, ISettingsText, string> _show;
        private IReadOnlyList<T> _options;

        public ChoiceRow(string id, ISettingValue<T> value, IReadOnlyList<T> options, Func<T, ISettingsText, string> show)
            : base(id)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _show = show ?? throw new ArgumentNullException(nameof(show));
            ResetsValue(value);
        }

        /// <summary>Options read each time the section opens (screen sizes, microphones).</summary>
        public ChoiceRow(string id, ISettingValue<T> value, Func<IReadOnlyList<T>> liveOptions,
            Func<T, ISettingsText, string> show) : this(id, value, Array.Empty<T>(), show)
        {
            _liveOptions = liveOptions ?? throw new ArgumentNullException(nameof(liveOptions));
            _options = liveOptions() ?? Array.Empty<T>();
        }

        public IReadOnlyList<T> Options => _options;

        public T Value => _value.Value;

        public override int Count => _options.Count;

        /// <summary>The value's option; the first when the value is none of them.</summary>
        public override int Index
        {
            get
            {
                if (_options.Count == 0) return -1;
                T value = _value.Value;
                for (int i = 0; i < _options.Count; i++)
                {
                    if (EqualityComparer<T>.Default.Equals(_options[i], value)) return i;
                }

                return 0;
            }
        }

        public override string OptionText(int option, ISettingsText text) => _show(_options[option], text);

        protected internal override object? Current => _value.Value;

        protected internal override Action? Remember() => RememberOf(_value);

        protected internal override void Open()
        {
            if (_liveOptions != null) _options = _liveOptions() ?? Array.Empty<T>();
        }

        protected internal override bool Step(int direction, ISettingsMenu menu) => Choose(Index + direction);

        protected internal override bool Submit(ISettingsMenu menu) =>
            Count > 0 && Choose(Index + 1 < Count ? Index + 1 : 0);

        private bool Choose(int option)
        {
            if (option < 0 || option >= _options.Count || option == Index) return false;
            _value.Value = _options[option];
            return true;
        }
    }

    /// <summary>Does something on Enter.</summary>
    public sealed class ActionRow : SettingsRow
    {
        private readonly Action<ISettingsMenu> _act;

        public ActionRow(string id, Action act) : base(id)
        {
            if (act == null) throw new ArgumentNullException(nameof(act));
            _act = _ => act();
        }

        internal ActionRow(string id, Action<ISettingsMenu> act) : base(id) => _act = act;

        protected internal override bool Submit(ISettingsMenu menu)
        {
            _act(menu);
            return true;
        }
    }

    /// <summary>A heading between rows, never focused.</summary>
    public sealed class HeadingRow : SettingsRow
    {
        public HeadingRow(string id) : base(id)
        {
        }

        public override bool Focusable => false;
    }

    /// <summary>
    /// Several keys of one action (Move: W A S D): left and right pick one, Enter listens for its new key. What a key
    /// is and how it is listened for is the deriving class's (the input integration's, or a game's).
    /// </summary>
    public abstract class KeysRow : SettingsRow
    {
        protected KeysRow(string id) : base(id)
        {
        }

        /// <summary>The picked key of this row's several.</summary>
        public int KeyFocus { get; internal set; }

        public abstract int Count { get; }

        public abstract string KeyText(int key);

        public abstract bool IsDefault(int key);

        /// <summary>The text key of the line shown while listening; its {0} is the row's label.</summary>
        protected internal virtual string ListenKey => "settings.listen";

        /// <summary>
        /// Waits for the new key of <paramref name="key"/> and takes it, asking the menu what it must (a swap). The menu
        /// calls it on Enter or a click; cancelled by Back or the menu's end. A throw other than cancelling is reported
        /// and the menu goes on.
        /// </summary>
        protected internal abstract UniTask ListenAsync(int key, ISettingsMenu menu, CancellationToken ct);

        protected internal override bool Step(int direction, ISettingsMenu menu)
        {
            int key = Math.Clamp(KeyFocus + direction, 0, Math.Max(0, Count - 1));
            if (key == KeyFocus) return false;
            KeyFocus = key;
            return true;
        }
    }
}
