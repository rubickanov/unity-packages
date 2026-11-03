using System;
using System.Collections.Generic;
using R3;

namespace Rubickanov.Settings
{
    /// <summary>
    /// One stored setting: a live value under an id, normalised on each set (a step kept in range, an option kept in its
    /// list), written and read through its <see cref="SettingFormat{T}"/>. An equal value changes nothing.
    /// </summary>
    public class Setting<T> : ISetting, ISettingValue<T>, IDisposable
    {
        private readonly SettingFormat<T> _format;
        private readonly Func<T, T>? _normalize;
        private readonly ReactiveProperty<T> _live;
        private readonly Subject<Unit> _changed = new();

        public Setting(string id, T @default, SettingFormat<T> format, Func<T, T>? normalize = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A setting needs an id.", nameof(id));
            Id = id;
            _format = format ?? throw new ArgumentNullException(nameof(format));
            _normalize = normalize;
            Default = Normalize(@default);
            _live = new ReactiveProperty<T>(Default);
        }

        public string Id { get; }

        public T Default { get; }

        public T Value
        {
            get => _live.Value;
            set
            {
                T next = Normalize(value);
                if (EqualityComparer<T>.Default.Equals(next, _live.Value)) return;
                _live.Value = next;
                _changed.OnNext(Unit.Default);
            }
        }

        /// <summary>The value at once on subscribing, then each change.</summary>
        public ReadOnlyReactiveProperty<T> Live => _live;

        public bool IsDefault => EqualityComparer<T>.Default.Equals(Value, Default);

        public Observable<Unit> Changed => _changed;

        public SettingFormat<T> Format => _format;

        public string Text => _format.Show(Value);

        public void Reset() => Value = Default;

        public bool TrySet(string text)
        {
            if (text == null || !_format.TryParse(text.Trim(), out T value)) return false;
            Value = value;
            return true;
        }

        public SettingsJsonValue Save() => _format.Write(Value);

        public bool TryLoad(SettingsJsonValue value)
        {
            if (!_format.TryRead(value, out T read)) return false;
            Value = read;
            return true;
        }

        public void Dispose()
        {
            _changed.Dispose();
            _live.Dispose();
        }

        public override string ToString() => $"{Id} = {Text}";

        private T Normalize(T value) => _normalize != null ? _normalize(value) : value;
    }

    /// <summary>Whole steps from <see cref="Min"/> to <see cref="Max"/> (a meter); what a step means is its applier's.</summary>
    public sealed class StepSetting : Setting<int>
    {
        public StepSetting(string id, int @default, int max = 10, int min = 0)
            : base(id, @default, SettingFormats.Int, value => Math.Clamp(value, min, Math.Max(min, max)))
        {
            Min = min;
            Max = Math.Max(min, max);
        }

        public int Min { get; }

        public int Max { get; }

        /// <summary>The value as a share of the range, 0 to 1.</summary>
        public float Share => Max > Min ? (Value - Min) / (float)(Max - Min) : 0f;
    }

    /// <summary>One of a fixed list; a value not in the list reads as the default.</summary>
    public sealed class ChoiceSetting<T> : Setting<T>
    {
        public ChoiceSetting(string id, T @default, IReadOnlyList<T> options, SettingFormat<T> format)
            : base(id, @default, format, value => Contains(options, value) ? value : @default)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public IReadOnlyList<T> Options { get; }

        private static bool Contains(IReadOnlyList<T> options, T value)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(options[i], value)) return true;
            }

            return false;
        }
    }

    /// <summary>
    /// A value the game keeps itself (its own file, a profile): rows edit it, the store never writes it. With a default,
    /// a section's reset puts it back.
    /// </summary>
    public sealed class Bound<T> : ISettingValue<T>, IResettable
    {
        private readonly Func<T> _get;
        private readonly Action<T> _set;
        private readonly bool _hasDefault;
        private readonly T _default = default!;

        public Bound(Func<T> get, Action<T> set)
        {
            _get = get ?? throw new ArgumentNullException(nameof(get));
            _set = set ?? throw new ArgumentNullException(nameof(set));
        }

        public Bound(Func<T> get, Action<T> set, T @default) : this(get, set)
        {
            _hasDefault = true;
            _default = @default;
        }

        public T Value
        {
            get => _get();
            set => _set(value);
        }

        /// <summary>Puts the default back; nothing without one.</summary>
        public void Reset()
        {
            if (_hasDefault) _set(_default);
        }
    }
}
