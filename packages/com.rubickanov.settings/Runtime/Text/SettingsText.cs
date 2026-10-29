using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Settings
{
    /// <summary>
    /// The settings screen's words by key, one table per language. Keys: a row's label is its id
    /// (<c>sound.master</c>), its line under the rows <c>id.about</c>, an option <c>id/option</c>
    /// (<c>display.window/windowed</c>), a section <c>section.id</c>, the package's own words <c>settings.*</c>.
    /// </summary>
    public interface ISettingsText
    {
        /// <summary>The text of a key; a missing one gives the key itself, warned once.</summary>
        string Get(string key);

        /// <summary>The text of a key, without a warning when there is none (optional lines such as <c>id.about</c>).</summary>
        bool TryGet(string key, out string text);
    }

    /// <summary>A table of the settings screen's words. <see cref="English"/> has the package's own.</summary>
    public sealed class SettingsText : ISettingsText
    {
        private readonly Dictionary<string, string> _lines = new();
        private readonly HashSet<string> _warned = new();
        private readonly Action<string> _logMissing;

        public SettingsText(Action<string>? logMissing = null) => _logMissing = logMissing ?? Debug.LogWarning;

        public SettingsText Add(string key, string text)
        {
            _lines[key] = text;
            return this;
        }

        /// <summary>A row's label and, when given, its line under the rows.</summary>
        public SettingsText Row(string id, string label, string? about = null)
        {
            _lines[id] = label;
            if (about != null) _lines[id + ".about"] = about;
            return this;
        }

        public SettingsText Option(string id, string option, string text)
        {
            _lines[id + "/" + option] = text;
            return this;
        }

        public SettingsText Section(string id, string title)
        {
            _lines["section." + id] = title;
            return this;
        }

        /// <summary>Takes every line of <paramref name="other"/>; its lines win.</summary>
        public SettingsText With(SettingsText other)
        {
            foreach (KeyValuePair<string, string> line in other._lines) _lines[line.Key] = line.Value;
            return this;
        }

        public string Get(string key)
        {
            if (_lines.TryGetValue(key, out string text)) return text;
            if (_warned.Add(key)) _logMissing($"Settings text has no line for {key}.");
            return key;
        }

        public bool TryGet(string key, out string text) => _lines.TryGetValue(key, out text);

        /// <summary>The package's words and its ready-made rows (default ids), in English.</summary>
        public static SettingsText English(Action<string>? logMissing = null) => new SettingsText(logMissing)
            .Add("settings.on", "ON")
            .Add("settings.off", "OFF")
            .Add("settings.yes", "YES")
            .Add("settings.no", "NO")
            .Add("settings.keep", "KEEP")
            .Add("settings.revert", "REVERT")
            .Add("settings.keep.question", "KEEP THIS SCREEN? BACK IN {0}")
            .Add("settings.swap.question", "{0} IS ON {1}. SWAP?")
            .Row("settings.reset", "RESET SECTION", "Puts this section back as it was.")
            .Row("settings.apply", "APPLY", "Puts the new screen on. Keep it, or it goes back by itself.")
            .Add("settings.reset.question", "PUT THIS SECTION BACK AS IT WAS?")
            .Add("settings.listen", "PRESS A KEY FOR {0}. ESC CANCELS")
            .Add("settings.listen.pad", "PRESS A BUTTON FOR {0}. START CANCELS")
            .Add("settings.keys.about", "{0}, THEN PRESS THE NEW KEY.")
            .Add("settings.keys.about.pad", "{0}, THEN PRESS THE NEW BUTTON.")
            .Add("settings.keys.about.ways", "LEFT AND RIGHT PICK THE WAY, {0} WAITS FOR ITS NEW KEY.")
            .Add("keys.heading.keys", "KEYS")
            .Add("keys.heading.pad", "BUTTONS")
            .Add("settings.native", "NATIVE")
            .Add("settings.nocap", "NONE")
            .Section("display", "DISPLAY")
            .Section("sound", "SOUND")
            .Section("controls", "CONTROLS")
            .Row("display.window", "WINDOW", "Full screen or a window.")
            .Option("display.window", "fullscreen", "FULLSCREEN")
            .Option("display.window", "windowed", "WINDOWED")
            .Row("display.size", "RESOLUTION", "The size of the picture.")
            .Row("display.vsync", "VSYNC", "Frames wait for the screen: no tearing.")
            .Row("display.frameCap", "FRAME CAP", "Most frames a second, without vsync.")
            .Row("look.mouse", "MOUSE SPEED", "How fast the mouse turns the camera.")
            .Row("look.pad", "STICK SPEED", "How fast the stick turns the camera.")
            .Row("look.invertY", "INVERT Y", "Up looks down.");
    }
}
