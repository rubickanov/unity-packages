using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace Rubickanov.Audio
{
    /// <summary>
    /// AudioMixer-based audio service with pooled SFX sources, music crossfade, and optional volume persistence via IAudioPersistence.
    /// </summary>
    public class UnityAudioService : IAudioService, IDisposable
    {
        private const string MasterVolumeParam = "MasterVolume";
        private const string MusicVolumeParam = "MusicVolume";
        private const string SFXVolumeParam = "SFXVolume";
        private readonly AudioMixer _mixer;
        private readonly AudioMixerGroup _musicGroup = default!;
        private readonly AudioMixerGroup _sfxGroup = default!;
        private readonly GameObject _root;
        private readonly AudioSource _musicSourceA;
        private readonly AudioSource _musicSourceB;
        private readonly float _crossfadeDuration;
        private readonly Queue<AudioSource> _sfxPool = new();
        private readonly LinkedList<AudioSource> _activeSources = new();
        private readonly Dictionary<AudioSource, LinkedListNode<AudioSource>> _activeNodes = new();
        private readonly Dictionary<int, AudioSource> _handleSources = new();
        private readonly IAudioPersistence? _persistence;
        private readonly CancellationTokenSource _cts = new();

        private int _nextHandleId = 1;
        private bool _musicSourceAActive = true;
        private CancellationTokenSource? _crossfadeCts;

        private float _masterVolume = 1f;
        private float _musicVolume = 1f;
        private float _sfxVolume = 1f;

        public float MasterVolume => _masterVolume;
        public float MusicVolume => _musicVolume;
        public float SFXVolume => _sfxVolume;

        private const string KeyMaster = "audio_master";
        private const string KeyMusic = "audio_music";
        private const string KeySfx = "audio_sfx";

        public UnityAudioService(AudioServiceConfig config, IAudioPersistence? persistence = null)
        {
            _persistence = persistence;
            _mixer = config.Mixer;
            _musicGroup = config.MusicGroup;
            _sfxGroup = config.SfxGroup;
            _crossfadeDuration = config.MusicCrossfadeDuration;

            _root = new GameObject("[AudioService]");
            Object.DontDestroyOnLoad(_root);

            _musicSourceA = CreateMusicSource("Music_A");
            _musicSourceB = CreateMusicSource("Music_B");
            _musicSourceB.volume = 0f;

            int maxSources = Mathf.Max(1, config.MaxSfxSources);
            for (int i = 0; i < maxSources; i++)
                _sfxPool.Enqueue(CreateSFXSource());

            SetMasterVolume(_persistence?.Load(KeyMaster, 1f) ?? 1f);
            SetMusicVolume(_persistence?.Load(KeyMusic, 1f) ?? 1f);
            SetSFXVolume(_persistence?.Load(KeySfx, 1f) ?? 1f);
        }

        private AudioSource CreateMusicSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform);
            var source = go.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            if (_musicGroup != null) source.outputAudioMixerGroup = _musicGroup;
            return source;
        }

        private AudioSource CreateSFXSource()
        {
            var child = new GameObject("SFX_Source");
            child.transform.SetParent(_root.transform);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            if (_sfxGroup != null) source.outputAudioMixerGroup = _sfxGroup;
            return source;
        }

        private AudioSource RentSource()
        {
            AudioSource source;

            if (_sfxPool.Count > 0)
            {
                source = _sfxPool.Dequeue();
            }
            else
            {
                var oldest = _activeSources.First!;
                source = oldest.Value;
                UntrackHandle(source);
                source.Stop();
                _activeSources.RemoveFirst();
                _activeNodes.Remove(source);
            }

            var node = _activeSources.AddLast(source);
            _activeNodes[source] = node;
            return source;
        }

        private void ReturnSource(AudioSource source)
        {
            source.Stop();
            source.resource = null;
            source.spatialBlend = 0f;
            source.loop = false;
            source.pitch = 1f;

            UntrackHandle(source);

            if (_activeNodes.Remove(source, out var node))
                _activeSources.Remove(node);

            _sfxPool.Enqueue(source);
        }

        private SoundHandle TrackHandle(AudioSource source)
        {
            int id = _nextHandleId++;
            _handleSources[id] = source;
            return new SoundHandle(id);
        }

        private void UntrackHandle(AudioSource source)
        {
            int? foundId = null;
            foreach (var kvp in _handleSources)
            {
                if (kvp.Value == source)
                {
                    foundId = kvp.Key;
                    break;
                }
            }

            if (foundId.HasValue)
                _handleSources.Remove(foundId.Value);
        }

        private async UniTaskVoid ReturnAfterPlayAsync(AudioSource source)
        {
            await UniTask.WaitWhile(() => source.isPlaying, cancellationToken: _cts.Token);
            ReturnSource(source);
        }

        private static void ApplyPitch(AudioSource source, in SoundConfig sound)
        {
            float variation = sound.PitchVariation;
            source.pitch = variation > 0f ? 1f + UnityEngine.Random.Range(-variation, variation) : 1f;
        }

        public SoundHandle PlaySFX(in SoundConfig sound, float volumeScale = 1f)
        {
            if (!sound.IsValid) return SoundHandle.Invalid;

            var source = RentSource();
            source.spatialBlend = 0f;
            source.resource = sound.Resource;
            source.volume = volumeScale;
            ApplyPitch(source, in sound);
            source.Play();

            var handle = TrackHandle(source);
            ReturnAfterPlayAsync(source).Forget();
            return handle;
        }

        public SoundHandle PlaySFXAtPoint(in SoundConfig sound, Vector3 position, float volumeScale = 1f)
        {
            if (!sound.IsValid) return SoundHandle.Invalid;

            var source = RentSource();
            source.transform.position = position;
            source.spatialBlend = 1f;
            source.resource = sound.Resource;
            source.volume = volumeScale;
            ApplyPitch(source, in sound);
            source.Play();

            var handle = TrackHandle(source);
            ReturnAfterPlayAsync(source).Forget();
            return handle;
        }

        public SoundHandle PlayLoop(in SoundConfig sound, float volumeScale = 1f)
        {
            if (!sound.IsValid) return SoundHandle.Invalid;

            var source = RentSource();
            source.spatialBlend = 0f;
            source.resource = sound.Resource;
            source.volume = volumeScale;
            source.loop = true;
            ApplyPitch(source, in sound);
            source.Play();

            return TrackHandle(source);
        }

        public void StopSound(SoundHandle handle)
        {
            if (!handle.IsValid) return;
            if (!_handleSources.Remove(handle.Id, out var source)) return;
            ReturnSource(source);
        }

        public void PlayMusic(in SoundConfig music)
        {
            if (!music.IsValid) return;

            var incoming = _musicSourceAActive ? _musicSourceB : _musicSourceA;
            var outgoing = _musicSourceAActive ? _musicSourceA : _musicSourceB;
            _musicSourceAActive = !_musicSourceAActive;

            incoming.resource = music.Resource;
            ApplyPitch(incoming, in music);
            incoming.Play();

            _crossfadeCts?.Cancel();
            _crossfadeCts?.Dispose();

            if (_crossfadeDuration > 0f && outgoing.isPlaying)
            {
                _crossfadeCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                CrossfadeAsync(outgoing, incoming, _crossfadeDuration, _crossfadeCts.Token).Forget();
            }
            else
            {
                outgoing.Stop();
                outgoing.resource = null;
                outgoing.volume = 0f;
                incoming.volume = 1f;
            }
        }

        private static async UniTaskVoid CrossfadeAsync(
            AudioSource outgoing, AudioSource incoming,
            float duration, CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (ct.IsCancellationRequested) return;

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                outgoing.volume = 1f - t;
                incoming.volume = t;
                await UniTask.Yield(ct);
            }

            outgoing.Stop();
            outgoing.resource = null;
            outgoing.volume = 0f;
            incoming.volume = 1f;
        }

        public void StopMusic()
        {
            _crossfadeCts?.Cancel();
            _crossfadeCts?.Dispose();
            _crossfadeCts = null;

            _musicSourceA.Stop();
            _musicSourceA.resource = null;
            _musicSourceA.volume = 0f;

            _musicSourceB.Stop();
            _musicSourceB.resource = null;
            _musicSourceB.volume = 0f;

            _musicSourceAActive = true;
        }

        public void SetMasterVolume(float volume01)
        {
            _masterVolume = Mathf.Clamp01(volume01);
            ApplyVolume(MasterVolumeParam, _masterVolume);
            _persistence?.Save(KeyMaster, _masterVolume);
        }

        public void SetMusicVolume(float volume01)
        {
            _musicVolume = Mathf.Clamp01(volume01);
            ApplyVolume(MusicVolumeParam, _musicVolume);
            _persistence?.Save(KeyMusic, _musicVolume);
        }

        public void SetSFXVolume(float volume01)
        {
            _sfxVolume = Mathf.Clamp01(volume01);
            ApplyVolume(SFXVolumeParam, _sfxVolume);
            _persistence?.Save(KeySfx, _sfxVolume);
        }

        private void ApplyVolume(string param, float volume01)
        {
            if (_mixer == null) return;
            float dB = volume01 > 0.0001f ? Mathf.Log10(volume01) * 20f : -80f;
            _mixer.SetFloat(param, dB);
        }

        public void Dispose()
        {
            _crossfadeCts?.Cancel();
            _crossfadeCts?.Dispose();
            _cts.Cancel();
            _cts.Dispose();
            if (_root != null) Object.Destroy(_root);
        }
    }
}
