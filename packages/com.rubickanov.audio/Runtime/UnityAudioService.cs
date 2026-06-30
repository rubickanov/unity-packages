using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Rubickanov.Storage;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace Rubickanov.Audio
{
    /// <summary>
    /// AudioMixer-based audio service with pooled SFX sources, music crossfade, and optional volume persistence via IStorageService.
    /// </summary>
    public class UnityAudioService : IAudioService, IDisposable
    {
        private readonly AudioMixer _mixer;
        private readonly AudioMixerGroup _musicGroup = default!;
        private readonly AudioMixerGroup _sfxGroup = default!;
        private readonly string _masterVolumeParam;
        private readonly string _musicVolumeParam;
        private readonly string _sfxVolumeParam;
        private readonly GameObject _root;
        private readonly AudioSource _musicSourceA;
        private readonly AudioSource _musicSourceB;
        private readonly float _crossfadeDuration;
        private readonly Queue<AudioSource> _sfxPool = new();
        private readonly LinkedList<AudioSource> _activeSources = new();
        private readonly Dictionary<AudioSource, LinkedListNode<AudioSource>> _activeNodes = new();
        private readonly Dictionary<long, AudioSource> _handleSources = new();
        private readonly Dictionary<AudioSource, long> _sourceHandles = new();
        private readonly Dictionary<AudioSource, CancellationTokenSource> _sourceWatchers = new();
        private readonly Dictionary<string, AudioSource> _loopSources = new();
        private readonly Dictionary<string, CancellationTokenSource> _loopWatchers = new();
        private readonly IStorageService? _storage;
        private readonly CancellationTokenSource _cts = new();

        private long _nextHandleId = 1;
        private bool _musicSourceAActive = true;
        private CancellationTokenSource? _crossfadeCts;
        private CancellationTokenSource? _duckCts;

        private float _masterVolume = 1f;
        private float _musicVolume = 1f;
        private float _sfxVolume = 1f;

        public float MasterVolume => _masterVolume;
        public float MusicVolume => _musicVolume;
        public float SFXVolume => _sfxVolume;

        private const string KeyMaster = "audio_master";
        private const string KeyMusic = "audio_music";
        private const string KeySfx = "audio_sfx";

        public UnityAudioService(AudioServiceConfig config, IStorageService? storage = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            _storage = storage;
            _mixer = config.Mixer;
            _musicGroup = config.MusicGroup;
            _sfxGroup = config.SfxGroup;
            _masterVolumeParam = config.MasterVolumeParam;
            _musicVolumeParam = config.MusicVolumeParam;
            _sfxVolumeParam = config.SfxVolumeParam;
            _crossfadeDuration = config.MusicCrossfadeDuration;

            _root = new GameObject("[AudioService]");
            if (Application.isPlaying)
                Object.DontDestroyOnLoad(_root);

            _musicSourceA = CreateMusicSource("Music_A");
            _musicSourceA.volume = 1f;
            _musicSourceB = CreateMusicSource("Music_B");
            _musicSourceB.volume = 0f;

            int maxSources = Mathf.Max(1, config.MaxSfxSources);
            for (int i = 0; i < maxSources; i++)
                _sfxPool.Enqueue(CreateSFXSource());

            SetMasterVolume(_storage?.GetFloat(KeyMaster, 1f) ?? 1f);
            SetMusicVolume(_storage?.GetFloat(KeyMusic, 1f) ?? 1f);
            SetSFXVolume(_storage?.GetFloat(KeySfx, 1f) ?? 1f);
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
            else if (_activeSources.Count > 0)
            {
                var oldest = _activeSources.First!;
                source = oldest.Value;
                EndWatch(source);
                UntrackHandle(source);
                source.Stop();
                _activeSources.RemoveFirst();
                _activeNodes.Remove(source);
            }
            else
            {
                // Pool drained and the active list is empty too: every source is in fade-out
                // limbo (StopSound removed it from _activeSources but FadeOutAndReclaimAsync
                // has not reclaimed it to the pool yet). There is nothing to dequeue or evict,
                // so grow a fresh source rather than dereferencing a null _activeSources.First.
                // The extra source reclaims back into the pool when it finishes, so the pool
                // settles at the real peak concurrency.
                source = CreateSFXSource();
            }

            var node = _activeSources.AddLast(source);
            _activeNodes[source] = node;
            return source;
        }

        private void ReturnSource(AudioSource source)
        {
            bool wasActive = _activeNodes.Remove(source, out var node);
            if (wasActive) _activeSources.Remove(node);

            EndWatch(source);
            UntrackHandle(source);

            ReclaimToPool(source);
        }

        private void ReclaimToPool(AudioSource source)
        {
            source.Stop();
            source.resource = null;
            source.spatialBlend = 0f;
            source.loop = false;
            source.pitch = 1f;
            source.volume = 1f;
            _sfxPool.Enqueue(source);
        }

        private CancellationToken BeginWatch(AudioSource source)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _sourceWatchers[source] = cts;
            return cts.Token;
        }

        private void EndWatch(AudioSource source)
        {
            if (_sourceWatchers.Remove(source, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        private SoundHandle TrackHandle(AudioSource source)
        {
            long id = _nextHandleId++;
            _handleSources[id] = source;
            _sourceHandles[source] = id;
            return new SoundHandle(id);
        }

        private void UntrackHandle(AudioSource source)
        {
            if (_sourceHandles.Remove(source, out var id))
                _handleSources.Remove(id);
        }

        private async UniTaskVoid ReturnAfterPlayAsync(AudioSource source, CancellationToken ct)
        {
            try
            {
                await UniTask.WaitWhile(() => source != null && source.isPlaying,
                    cancellationToken: ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return;
            }

            if (source != null)
                ReturnSource(source);
        }

        private static void ApplyPitch(AudioSource source, in SoundConfig sound)
        {
            float variation = sound.PitchVariation;
            source.pitch = variation > 0f ? 1f + UnityEngine.Random.Range(-variation, variation) : 1f;
        }

        public SoundHandle PlaySFX(in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f)
        {
            if (!sound.IsValid) return SoundHandle.Invalid;

            var source = RentSource();
            source.spatialBlend = 0f;
            source.resource = sound.Resource;
            ApplyPitch(source, in sound);
            var watch = BeginWatch(source);
            StartPlayWithFade(source, volumeScale, fadeIn, watch);

            var handle = TrackHandle(source);
            ReturnAfterPlayAsync(source, watch).Forget();
            return handle;
        }

        public SoundHandle PlaySFXAtPoint(in SoundConfig sound, Vector3 position, float volumeScale = 1f, float fadeIn = 0f)
        {
            if (!sound.IsValid) return SoundHandle.Invalid;

            var source = RentSource();
            source.transform.position = position;
            source.spatialBlend = 1f;
            source.resource = sound.Resource;
            ApplyPitch(source, in sound);
            var watch = BeginWatch(source);
            StartPlayWithFade(source, volumeScale, fadeIn, watch);

            var handle = TrackHandle(source);
            ReturnAfterPlayAsync(source, watch).Forget();
            return handle;
        }

        public SoundHandle PlaySFXAttached(in SoundConfig sound, Transform follow, float volumeScale = 1f, float fadeIn = 0f)
        {
            if (!sound.IsValid) return SoundHandle.Invalid;
            if (follow == null) return SoundHandle.Invalid;

            var source = RentSource();
            source.transform.position = follow.position;
            source.spatialBlend = 1f;
            source.resource = sound.Resource;
            ApplyPitch(source, in sound);
            var watch = BeginWatch(source);
            StartPlayWithFade(source, volumeScale, fadeIn, watch);

            var handle = TrackHandle(source);
            FollowAndReturnAsync(source, follow, watch).Forget();
            return handle;
        }

        private void StartPlayWithFade(AudioSource source, float targetVolume, float fadeIn, CancellationToken watch)
        {
            if (fadeIn > 0f)
            {
                source.volume = 0f;
                source.Play();
                // Tie the fade to the per-source watcher token, not the service-lifetime _cts:
                // if the source is returned, evicted, or stopped mid-fade, EndWatch cancels this
                // token so the fade stops writing volume to what is now a recycled source playing
                // a different sound.
                FadeInAsync(source, targetVolume, fadeIn, watch).Forget();
            }
            else
            {
                source.volume = targetVolume;
                source.Play();
            }
        }

        private static async UniTaskVoid FadeInAsync(AudioSource source, float targetVolume, float duration, CancellationToken ct)
        {
            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    if (source != null) source.volume = targetVolume * t;
                    await UniTask.Yield(ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Debug.LogException(ex); return; }

            if (source != null) source.volume = targetVolume;
        }

        private async UniTaskVoid FollowAndReturnAsync(AudioSource source, Transform follow, CancellationToken ct)
        {
            try
            {
                while (source != null && source.isPlaying)
                {
                    if (follow != null)
                        source.transform.position = follow.position;

                    await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return;
            }

            if (source != null)
                ReturnSource(source);
        }

        public void PlayLoop(string slot, in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f)
        {
            if (string.IsNullOrEmpty(slot)) return;
            if (!sound.IsValid) return;

            CancelLoopWatcher(slot);

            if (!_loopSources.TryGetValue(slot, out var source))
            {
                source = CreateLoopSource(slot);
                _loopSources[slot] = source;
            }

            source.Stop();
            source.spatialBlend = 0f;
            source.resource = sound.Resource;
            source.loop = true;
            ApplyPitch(source, in sound);

            if (fadeIn > 0f)
            {
                source.volume = 0f;
                source.Play();
                var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                _loopWatchers[slot] = cts;
                FadeInAsync(source, volumeScale, fadeIn, cts.Token).Forget();
            }
            else
            {
                source.volume = volumeScale;
                source.Play();
            }
        }

        public void StopLoop(string slot, float fadeOut = 0f)
        {
            if (string.IsNullOrEmpty(slot)) return;
            if (!_loopSources.TryGetValue(slot, out var source)) return;

            CancelLoopWatcher(slot);

            if (fadeOut > 0f)
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                _loopWatchers[slot] = cts;
                FadeOutLoopAsync(slot, source, fadeOut, cts.Token).Forget();
            }
            else
            {
                source.Stop();
                source.resource = null;
            }
        }

        public bool IsLoopPlaying(string slot)
        {
            if (string.IsNullOrEmpty(slot)) return false;
            return _loopSources.TryGetValue(slot, out var source) && source != null && source.isPlaying;
        }

        private void CancelLoopWatcher(string slot)
        {
            if (_loopWatchers.Remove(slot, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        private async UniTaskVoid FadeOutLoopAsync(string slot, AudioSource source, float duration, CancellationToken ct)
        {
            try
            {
                float startVolume = source != null ? source.volume : 0f;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    if (source != null) source.volume = startVolume * (1f - t);
                    await UniTask.Yield(ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Debug.LogException(ex); return; }

            if (source != null)
            {
                source.Stop();
                source.resource = null;
            }
            if (_loopWatchers.TryGetValue(slot, out var stored) && !stored.Token.IsCancellationRequested)
            {
                _loopWatchers.Remove(slot);
                stored.Dispose();
            }
        }

        private AudioSource CreateLoopSource(string slot)
        {
            var child = new GameObject($"Loop_{slot}");
            child.transform.SetParent(_root.transform);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            if (_sfxGroup != null) source.outputAudioMixerGroup = _sfxGroup;
            return source;
        }

        public void StopSound(SoundHandle handle, float fadeOut = 0f)
        {
            if (!handle.IsValid) return;
            if (!_handleSources.TryGetValue(handle.Id, out var source)) return;

            if (fadeOut > 0f)
            {
                if (_activeNodes.Remove(source, out var node))
                    _activeSources.Remove(node);
                EndWatch(source);
                UntrackHandle(source);
                FadeOutAndReclaimAsync(source, fadeOut, _cts.Token).Forget();
            }
            else
            {
                ReturnSource(source);
            }
        }

        private async UniTaskVoid FadeOutAndReclaimAsync(AudioSource source, float duration, CancellationToken ct)
        {
            try
            {
                float startVolume = source != null ? source.volume : 0f;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    if (source != null) source.volume = startVolume * (1f - t);
                    await UniTask.Yield(ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Debug.LogException(ex); return; }

            if (source != null)
                ReclaimToPool(source);
        }

        public void PlayMusic(in MusicConfig music, float? crossfadeDuration = null)
        {
            if (!music.IsValid) return;

            _crossfadeCts?.Cancel();
            _crossfadeCts?.Dispose();
            _crossfadeCts = null;

            var incoming = _musicSourceAActive ? _musicSourceB : _musicSourceA;
            var outgoing = _musicSourceAActive ? _musicSourceA : _musicSourceB;
            _musicSourceAActive = !_musicSourceAActive;

            float outgoingStartVolume = outgoing.isPlaying ? outgoing.volume : 0f;
            float duration = crossfadeDuration ?? _crossfadeDuration;

            incoming.resource = music.Resource;
            incoming.pitch = 1f;
            incoming.volume = 0f;
            incoming.Play();

            if (duration > 0f && outgoingStartVolume > 0f)
            {
                _crossfadeCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                CrossfadeAsync(outgoing, incoming, outgoingStartVolume, duration, _crossfadeCts.Token).Forget();
            }
            else
            {
                outgoing.Stop();
                outgoing.resource = null;
                outgoing.volume = 0f;
                incoming.volume = 1f;
            }
        }

        public void TransitionToSnapshot(string snapshotName, float duration)
        {
            if (_mixer == null || string.IsNullOrEmpty(snapshotName)) return;

            var snapshot = _mixer.FindSnapshot(snapshotName);
            if (snapshot == null)
            {
                Debug.LogWarning($"[AudioService] Snapshot '{snapshotName}' not found on mixer.");
                return;
            }
            snapshot.TransitionTo(Mathf.Max(0f, duration));
        }

        private static async UniTaskVoid CrossfadeAsync(
            AudioSource outgoing, AudioSource incoming,
            float outgoingStartVolume, float duration, CancellationToken ct)
        {
            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    if (outgoing != null) outgoing.volume = outgoingStartVolume * (1f - t);
                    if (incoming != null) incoming.volume = t;
                    await UniTask.Yield(ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return;
            }

            if (outgoing != null)
            {
                outgoing.Stop();
                outgoing.resource = null;
                outgoing.volume = 0f;
            }
            if (incoming != null) incoming.volume = 1f;
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
            ApplyVolume(_masterVolumeParam, _masterVolume);
            _storage?.SetFloat(KeyMaster, _masterVolume).Forget();
        }

        public void SetMusicVolume(float volume01)
        {
            _musicVolume = Mathf.Clamp01(volume01);
            ApplyVolume(_musicVolumeParam, _musicVolume);
            _storage?.SetFloat(KeyMusic, _musicVolume).Forget();
        }

        public void SetSFXVolume(float volume01)
        {
            _sfxVolume = Mathf.Clamp01(volume01);
            ApplyVolume(_sfxVolumeParam, _sfxVolume);
            _storage?.SetFloat(KeySfx, _sfxVolume).Forget();
        }

        private void ApplyVolume(string param, float volume01)
        {
            if (_mixer == null || string.IsNullOrEmpty(param)) return;
            float dB = volume01 > 0.0001f ? Mathf.Log10(volume01) * 20f : -80f;
            if (!_mixer.SetFloat(param, dB))
                Debug.LogWarning($"[AudioService] Mixer parameter '{param}' is not exposed.");
        }

        public void DuckSFX(float amount01, float duration, float attack = 0.05f, float release = 0.3f)
        {
            if (_mixer == null || string.IsNullOrEmpty(_sfxVolumeParam)) return;
            if (duration <= 0f) return;

            _duckCts?.Cancel();
            _duckCts?.Dispose();
            _duckCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

            float target = Mathf.Clamp01(amount01) * _sfxVolume;
            DuckAsync(target, duration, Mathf.Max(0f, attack), Mathf.Max(0f, release), _duckCts.Token).Forget();
        }

        private async UniTaskVoid DuckAsync(float targetVolume, float hold, float attack, float release, CancellationToken ct)
        {
            try
            {
                float startVolume = _sfxVolume;
                float elapsed = 0f;
                while (elapsed < attack)
                {
                    elapsed += Time.deltaTime;
                    float t = attack > 0f ? Mathf.Clamp01(elapsed / attack) : 1f;
                    ApplyVolume(_sfxVolumeParam, Mathf.Lerp(startVolume, targetVolume, t));
                    await UniTask.Yield(ct);
                }
                ApplyVolume(_sfxVolumeParam, targetVolume);

                await UniTask.Delay(TimeSpan.FromSeconds(hold), cancellationToken: ct);

                elapsed = 0f;
                while (elapsed < release)
                {
                    elapsed += Time.deltaTime;
                    float t = release > 0f ? Mathf.Clamp01(elapsed / release) : 1f;
                    ApplyVolume(_sfxVolumeParam, Mathf.Lerp(targetVolume, _sfxVolume, t));
                    await UniTask.Yield(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // A cancelled duck (superseded by another DuckSFX or torn down) must not leave the
                // shared SFX mixer param attenuated — restore the baseline before bailing.
                ApplyVolume(_sfxVolumeParam, _sfxVolume);
                return;
            }
            catch (Exception ex) { Debug.LogException(ex); }

            ApplyVolume(_sfxVolumeParam, _sfxVolume);
        }

        public void Dispose()
        {
            _crossfadeCts?.Cancel();
            _crossfadeCts?.Dispose();

            if (_duckCts != null)
            {
                // The duck coroutine's cancellation handler runs on a later frame (async), but the
                // mixer is externally owned and outlives this service — restore the SFX param
                // synchronously so disposing mid-duck doesn't leave it permanently attenuated.
                _duckCts.Cancel();
                _duckCts.Dispose();
                _duckCts = null;
                ApplyVolume(_sfxVolumeParam, _sfxVolume);
            }

            foreach (var kvp in _sourceWatchers)
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }
            _sourceWatchers.Clear();

            foreach (var kvp in _loopWatchers)
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }
            _loopWatchers.Clear();

            _cts.Cancel();
            _cts.Dispose();
            if (_root != null)
            {
                if (Application.isPlaying) Object.Destroy(_root);
                else Object.DestroyImmediate(_root);
            }
        }
    }
}
