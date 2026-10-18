using System;

namespace Rubickanov.Settings
{
    /// <summary>How the settings file is kept.</summary>
    public sealed class SettingsFileOptions
    {
        /// <summary>This build's format, written as <c>"format"</c>; a file without one is format 0.</summary>
        public int Format = 1;

        /// <summary>Rewrites an older file's values before the settings take them.</summary>
        public SettingsMigration? Migrate;

        /// <summary>Writes on <c>Application.quitting</c> (on desktop the write ends before the game does).</summary>
        public bool SaveOnQuit = true;

        /// <summary>Unity's error log when null.</summary>
        public Action<string>? LogError;

        /// <summary>Unity's warning log when null.</summary>
        public Action<string>? LogWarning;
    }

    /// <summary>
    /// Rewrites an older file's values in place (rename, convert, remove); false when it cannot: the file is kept aside
    /// and the defaults start.
    /// </summary>
    public delegate bool SettingsMigration(int fromFormat, SettingsValues values);
}
