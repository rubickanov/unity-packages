using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using Rubickanov.Save;
using UnityEngine;

namespace Rubickanov.Settings
{
    /// <summary>
    /// The player's settings as the game declares them, kept by id in one save document. The values are their defaults
    /// till <see cref="ReadAsync"/> is done (at once on desktop), then what was kept; a setting declared later takes its
    /// kept value at once. Ids nobody declared are written back as they were read, so a file outlives builds that know
    /// more or fewer settings. Written by <see cref="SaveAsync"/> (when a settings screen closes) and on quit, and only
    /// when the text differs from what was last read or written. A file that cannot be read is kept aside and the
    /// defaults start.
    /// </summary>
    public sealed class SettingsStore : IDisposable
    {
        private readonly SaveDocument _file;
        private readonly SettingsFileOptions _options;
        private readonly Action<string> _logError;
        private readonly Action<string> _logWarning;
        private readonly List<ISetting> _all = new();
        private readonly Dictionary<string, ISetting> _byId = new();
        private readonly List<IDisposable> _owned = new();
        private readonly SettingsValues _kept = new();
        private readonly List<KeyValuePair<string, SettingsJsonValue>> _writing = new();
        private readonly Subject<ISetting> _changed = new();
        private string? _saved;
        private bool _reading;
        private bool _disposed;

        public SettingsStore(ISaveStore store, SaveKey key, SettingsFileOptions? options = null)
        {
            _options = options ?? new SettingsFileOptions();
            _logError = _options.LogError ?? Debug.LogError;
            _logWarning = _options.LogWarning ?? Debug.LogWarning;
            _file = new SaveDocument(store, key, _logError);
            if (_options.SaveOnQuit) Application.quitting += Quit;
        }

        /// <summary>Every declared setting, in declaration order.</summary>
        public IReadOnlyList<ISetting> All => _all;

        /// <summary>Any declared setting changed.</summary>
        public Observable<ISetting> Changed => _changed;

        /// <summary>The kept values have been taken (or there were none).</summary>
        public bool IsRead { get; private set; }

        /// <summary>Where the settings are kept, for logs and the console.</summary>
        public string Where => _file.Where;

        /// <summary>Where a file that could not be read is kept aside.</summary>
        public string BrokenWhere => _file.BrokenWhere;

        /// <summary>Declares a setting; a second one with the same id throws.</summary>
        public T Add<T>(T setting) where T : ISetting
        {
            if (setting == null) throw new ArgumentNullException(nameof(setting));
            if (_byId.ContainsKey(setting.Id)) throw new ArgumentException($"A setting {setting.Id} is already declared.");
            if (setting.Id == SettingsJson.FormatField) throw new ArgumentException($"{setting.Id} is the file's own field.");

            bool unchanged = IsRead && _saved == Text();
            _all.Add(setting);
            _byId.Add(setting.Id, setting);
            if (IsRead && _kept.TryGet(setting.Id, out SettingsJsonValue value)) Load(setting, value);
            _owned.Add(setting.Changed.Subscribe(_ => _changed.OnNext(setting)));
            if (setting is IDisposable disposable) _owned.Add(disposable);

            // A late declaration taking what the file has is not a change of the player's.
            if (unchanged) _saved = Text();
            return setting;
        }

        /// <summary>Whole steps, <paramref name="min"/> to <paramref name="max"/>.</summary>
        public StepSetting Steps(string id, int @default, int max = 10, int min = 0) =>
            Add(new StepSetting(id, @default, max, min));

        public Setting<bool> Toggle(string id, bool @default) => Add(new Setting<bool>(id, @default, SettingFormats.Bool));

        public Setting<T> Enum<T>(string id, T @default) where T : struct, System.Enum =>
            Add(new Setting<T>(id, @default, SettingFormats.Enum<T>()));

        public ChoiceSetting<T> Choice<T>(string id, T @default, IReadOnlyList<T> options, SettingFormat<T> format) =>
            Add(new ChoiceSetting<T>(id, @default, options, format));

        public Setting<float> Range(string id, float @default, float min, float max) =>
            Add(new Setting<float>(id, @default, SettingFormats.Float, value => Mathf.Clamp(value, min, max)));

        public Setting<string> Text(string id, string @default) => Add(new Setting<string>(id, @default, SettingFormats.String));

        public ISetting? Find(string id) => id != null && _byId.TryGetValue(id, out ISetting setting) ? setting : null;

        /// <summary>The settings under a prefix: <c>"sound"</c> gives <c>sound.*</c>.</summary>
        public IEnumerable<ISetting> Under(string prefix)
        {
            string start = prefix.EndsWith(".") ? prefix : prefix + ".";
            foreach (ISetting setting in _all)
            {
                if (setting.Id.StartsWith(start, StringComparison.Ordinal)) yield return setting;
            }
        }

        /// <summary>Reads the file once and the declared settings take its values.</summary>
        public UniTask ReadAsync()
        {
            if (_reading) return UniTask.CompletedTask;
            _reading = true;
            return _file.ReadAsync(Take);
        }

        /// <summary>Writes the settings if their text differs from what was last read or written; true when it did.</summary>
        public async UniTask<bool> SaveAsync()
        {
            if (_disposed) return false;
            string? written = await _file.WriteAsync(() =>
            {
                string text = Text();
                return text != _saved ? text : null;
            });
            if (written == null) return false;
            _saved = written;
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Application.quitting -= Quit;
            foreach (IDisposable owned in _owned) owned.Dispose();
            _owned.Clear();
            _changed.Dispose();
        }

        /// <summary>The file as it would be written now.</summary>
        internal string Text()
        {
            _writing.Clear();
            foreach (ISetting setting in _all)
            {
                _writing.Add(new KeyValuePair<string, SettingsJsonValue>(setting.Id, setting.Save()));
            }

            for (int i = 0; i < _kept.Count; i++)
            {
                string id = _kept.IdAt(i);
                if (!_byId.ContainsKey(id) && _kept.TryGet(id, out SettingsJsonValue value))
                {
                    _writing.Add(new KeyValuePair<string, SettingsJsonValue>(id, value));
                }
            }

            return SettingsJson.Write(_options.Format, _writing);
        }

        // On desktop the write is done before the game goes.
        private void Quit() => SaveAsync().Forget();

        private void Take(string? text)
        {
            if (_disposed) return;
            if (text != null && !TryTake(text))
            {
                _kept.Clear();
                _logError($"{Where} could not be read, the settings start from their defaults; the file is kept as {BrokenWhere}.");
                _file.KeepBrokenAsync(text).Forget();
            }

            IsRead = true;
            _saved = Text();
        }

        private bool TryTake(string text)
        {
            int format;
            try
            {
                format = SettingsJson.Read(text, _kept);
            }
            catch (FormatException e)
            {
                _logError($"{Where}: {e.Message}");
                return false;
            }

            if (format > _options.Format)
            {
                _logWarning($"{Where} is format {format}, this build writes {_options.Format}: read by id as it is.");
            }
            else if (format < _options.Format && _options.Migrate != null && !_options.Migrate(format, _kept))
            {
                _logError($"{Where}: format {format} could not be brought to {_options.Format}.");
                return false;
            }

            foreach (ISetting setting in _all)
            {
                if (_kept.TryGet(setting.Id, out SettingsJsonValue value)) Load(setting, value);
            }

            return true;
        }

        private void Load(ISetting setting, SettingsJsonValue value)
        {
            if (!setting.TryLoad(value))
            {
                _logWarning($"{Where}: {setting.Id} = {value} is not a value of it; it stays {setting.Text}.");
            }
        }
    }
}
