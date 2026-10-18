using System;
using R3;

namespace Rubickanov.Settings
{
    /// <summary>Something a section's reset puts back.</summary>
    public interface IResettable
    {
        void Reset();
    }

    /// <summary>One stored setting, untyped: what the store, the console and a reset need.</summary>
    public interface ISetting : IResettable
    {
        /// <summary>Dotted id, <c>sound.master</c>: the dots nest it in the file.</summary>
        string Id { get; }

        bool IsDefault { get; }

        /// <summary>Fires after each change of the value.</summary>
        Observable<Unit> Changed { get; }

        /// <summary>The value as the console shows it: <c>7</c>, <c>windowed</c>, <c>1920x1080</c>.</summary>
        string Text { get; }

        /// <summary>The console's set; false when the text is no value of this setting.</summary>
        bool TrySet(string text);

        /// <summary>The value as the file keeps it.</summary>
        SettingsJsonValue Save();

        /// <summary>Takes a kept value; false keeps the value as it was.</summary>
        bool TryLoad(SettingsJsonValue value);
    }

    /// <summary>What a row edits: a stored setting, or a value the game keeps elsewhere (<see cref="Bound{T}"/>).</summary>
    public interface ISettingValue<T>
    {
        T Value { get; set; }
    }
}
