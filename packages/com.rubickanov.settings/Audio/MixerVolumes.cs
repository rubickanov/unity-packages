using System;
using System.Collections.Generic;
using R3;
using Rubickanov.Audio;

namespace Rubickanov.Settings.Audio
{
    /// <summary>
    /// Volumes as steps 0–10 (default 10), each sent to a mixer's exposed parameter as a share (<c>SetVolume</c> turns
    /// it into decibels). The game names the ids and the parameters.
    /// </summary>
    public sealed class MixerVolumes : IDisposable
    {
        private readonly IAudioService _audio;
        private readonly List<StepSetting> _all = new();
        private readonly List<string> _parameters = new();
        private readonly CompositeDisposable _subscriptions = new();

        /// <example><code>new MixerVolumes(store, audio, ("sound.master", "MasterVolume"), ("sound.music", "MusicVolume"))</code></example>
        public MixerVolumes(SettingsStore store, IAudioService audio, params (string id, string parameter)[] volumes)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            foreach ((string id, string parameter) in volumes)
            {
                _all.Add(store.Steps(id, 10));
                _parameters.Add(parameter);
            }
        }

        public IReadOnlyList<StepSetting> All => _all;

        /// <summary>The volume of a mixer parameter; throws for one not given.</summary>
        public StepSetting Of(string parameter)
        {
            int index = _parameters.IndexOf(parameter);
            if (index < 0) throw new ArgumentException($"No volume for {parameter}.", nameof(parameter));
            return _all[index];
        }

        /// <summary>Sends every volume now and on each change.</summary>
        public void Start()
        {
            for (int i = 0; i < _all.Count; i++)
            {
                StepSetting volume = _all[i];
                string parameter = _parameters[i];
                _subscriptions.Add(volume.Live.Subscribe(_ => _audio.SetVolume(parameter, volume.Share)));
            }
        }

        /// <summary>A meter per volume, in the order given.</summary>
        public void AddRows(SectionBuilder section)
        {
            foreach (StepSetting volume in _all) section.Steps(volume);
        }

        public void Dispose() => _subscriptions.Dispose();
    }
}
