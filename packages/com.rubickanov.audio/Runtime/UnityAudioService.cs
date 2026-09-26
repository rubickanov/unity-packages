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
    /// AudioMixer-based audio service with pooled SFX sources, loop slots and music crossfade. The service writes
    /// no volume until asked; saving and restoring volumes is the caller's job. A full pool evicts the lowest
    /// priority one-shot, oldest first; <see cref="SoundConfig.MaxInstances"/> and
    /// <see cref="SoundConfig.MinInterval"/> drop repeats of one sound before they reach the pool.
    /// </summary>
    public class UnityAudioService : IAudioService, IDisposable
    {
        private readonly AudioMixer _mixer;
        private readonly AudioMixerGroup _musicGroup = default!;
        private readonly AudioMixerGroup _sfxGroup = default!;
        private readonly GameObject _root;
        private readonly AudioSource _musicSourceA;
        private readonly AudioSource _musicSourceB;
        private readonly float _crossfadeDuration;
        private readonly bool _unscaledTime;
        private readonly AudioRolloffMode _rolloff;
        private readonly float _minDistance;
        private readonly float _maxDistance;
        private readonly float _dopplerLevel;
        private readonly Queue<AudioSource> _sfxPool = new();
        private readonly LinkedList<AudioSource> _activeSources = new();
        private readonly Dictionary<AudioSource, LinkedListNode<AudioSource>> _activeNodes = new();
        private readonly Dictionary<AudioSource, Voice> _voices = new();
        private readonly Dictionary<AudioResource, int> _instanceCounts = new();
        private readonly Dictionary<AudioResource, double> _lastStarts = new();
        private readonly Dictionary<long, AudioSource> _handleSources = new();
        private readonly Dictionary<AudioSource, long> _sourceHandles = new();
        private readonly Dictionary<AudioSource, CancellationTokenSource> _sourceWatchers = new();
        private readonly Dictionary<string, AudioSource> _loopSources = new();
        private readonly Dictionary<string, CancellationTokenSource> _loopWatchers = new();
        private readonly Dictionary<string, CancellationTokenSource> _loopPitchWatchers = new();
        private readonly HashSet<string> _stoppingLoops = new();
        private readonly List<AudioSource> _stopBuffer = new();
        private readonly Dictionary<string, float> _volumes = new();
        private readonly CancellationTokenSource _cts = new();

        // Clock for MinInterval, the same time the fades run on.
        private readonly Func<double> _now;

        private long _nextHandleId = 1;
        private bool _musicSourceAActive = true;
        private CancellationTokenSource? _crossfadeCts;

        public UnityAudioService(AudioServiceConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            _mixer = config.Mixer;
            _musicGroup = config.MusicGroup;
            _sfxGroup = config.SfxGroup;
            _crossfadeDuration = config.MusicCrossfadeDuration;
            _unscaledTime = config.UnscaledTime;
            _rolloff = config.Rolloff;
            _minDistance = config.MinDistance;
            _maxDistance = Mathf.Max(config.MinDistance, config.MaxDistance);
            _dopplerLevel = config.DopplerLevel;
            _now = _unscaledTime ? () => Time.realtimeSinceStartupAsDouble : () => Time.timeAsDouble;

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
        }

        // Slow motion must not stretch fades, and a zero time scale must not freeze them.
        private float DeltaTime => _unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

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
            Apply3DSettings(source);
            return source;
        }

        // The pool never changes these per sound, so setting them once per source is enough.
        private void Apply3DSettings(AudioSource source)
        {
            source.rolloffMode = _rolloff;
            source.minDistance = _minDistance;
            source.maxDistance = _maxDistance;
            source.dopplerLevel = _dopplerLevel;
        }

        // Null when the sound may not play: invalid, over its repeat limit, or below every sound in a full pool.
        private AudioSource? RentSource(in SoundConfig sound)
        {
            if (!sound.IsValid || !PassesRepeatLimit(in sound)) return null;

            AudioSource source;

            if (_sfxPool.Count > 0)
            {
                source = _sfxPool.Dequeue();
            }
            else if (_activeSources.Count > 0)
            {
                // AudioSource.priority only decides which voices Unity mutes; who leaves the pool is decided here.
                var victim = LowestPriorityVoice();
                if (_voices[victim].Priority > sound.Priority) return null;

                source = victim;
                Unlink(source);
                EndWatch(source);
                UntrackHandle(source);
                source.Stop();
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
            _voices[source] = new Voice(sound.Priority, sound.Resource);
            _instanceCounts[sound.Resource] = InstanceCount(sound.Resource) + 1;
            _lastStarts[sound.Resource] = _now();
            return source;
        }

        private bool PassesRepeatLimit(in SoundConfig sound)
        {
            if (sound.MaxInstances > 0 && InstanceCount(sound.Resource) >= sound.MaxInstances) return false;
            if (sound.MinInterval > 0f && _lastStarts.TryGetValue(sound.Resource, out var last) &&
                _now() - last < sound.MinInterval) return false;
            return true;
        }

        private int InstanceCount(AudioResource resource) =>
            _instanceCounts.TryGetValue(resource, out var count) ? count : 0;

        // The active list runs oldest to newest, so the first lowest found is the oldest of them.
        private AudioSource LowestPriorityVoice()
        {
            var node = _activeSources.First!;
            var lowest = node.Value;
            int lowestPriority = _voices[lowest].Priority;
            for (node = node.Next; node != null; node = node.Next)
            {
                int priority = _voices[node.Value].Priority;
                if (priority < lowestPriority)
                {
                    lowest = node.Value;
                    lowestPriority = priority;
                }
            }
            return lowest;
        }

        // Takes the source out of the active list and its sound's copy count; a fading-out source is no longer a copy.
        private void Unlink(AudioSource source)
        {
            if (_activeNodes.Remove(source, out var node))
                _activeSources.Remove(node);

            if (_voices.Remove(source, out var voice))
            {
                int left = InstanceCount(voice.Resource) - 1;
                if (left > 0) _instanceCounts[voice.Resource] = left;
                else _instanceCounts.Remove(voice.Resource);
            }
        }

        private void ReturnSource(AudioSource source)
        {
            Unlink(source);
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

        private void ApplyOutput(AudioSource source, in SoundConfig sound)
        {
            source.outputAudioMixerGroup = sound.Output != null ? sound.Output : _sfxGroup;
        }

        private static void ApplyPitch(AudioSource source, in SoundConfig sound)
        {
            float variation = sound.PitchVariation;
            source.pitch = variation > 0f ? 1f + UnityEngine.Random.Range(-variation, variation) : 1f;
        }

        public SoundHandle PlaySFX(in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f)
        {
            var source = RentSource(in sound);
            if (source == null) return SoundHandle.Invalid;

            source.spatialBlend = 0f;
            return Start(source, in sound, volumeScale, fadeIn, follow: null);
        }

        public SoundHandle PlaySFXAtPoint(in SoundConfig sound, Vector3 position, float volumeScale = 1f, float fadeIn = 0f)
        {
            var source = RentSource(in sound);
            if (source == null) return SoundHandle.Invalid;

            source.transform.position = position;
            source.spatialBlend = 1f;
            return Start(source, in sound, volumeScale, fadeIn, follow: null);
        }

        public SoundHandle PlaySFXAttached(in SoundConfig sound, Transform follow, float volumeScale = 1f, float fadeIn = 0f)
        {
            if (follow == null) return SoundHandle.Invalid;
            var source = RentSource(in sound);
            if (source == null) return SoundHandle.Invalid;

            source.transform.position = follow.position;
            source.spatialBlend = 1f;
            return Start(source, in sound, volumeScale, fadeIn, follow);
        }

        private SoundHandle Start(AudioSource source, in SoundConfig sound, float volumeScale, float fadeIn, Transform? follow)
        {
            source.resource = sound.Resource;
            ApplyOutput(source, in sound);
            ApplyPitch(source, in sound);
            var watch = BeginWatch(source);
            StartPlayWithFade(source, volumeScale, fadeIn, watch);

            var handle = TrackHandle(source);
            if (follow != null) FollowAndReturnAsync(source, follow, watch).Forget();
            else ReturnAfterPlayAsync(source, watch).Forget();
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

        private async UniTaskVoid FadeInAsync(AudioSource source, float targetVolume, float duration, CancellationToken ct)
        {
            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += DeltaTime;
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
            CancelWatcher(_loopPitchWatchers, slot);
            _stoppingLoops.Remove(slot);

            if (!_loopSources.TryGetValue(slot, out var source))
            {
                source = CreateLoopSource(slot);
                _loopSources[slot] = source;
            }

            source.Stop();
            source.spatialBlend = 0f;
            source.resource = sound.Resource;
            source.loop = true;
            ApplyOutput(source, in sound);
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
                // A fading-out loop is on its way to silence: SetLoopVolume must not revive it.
                _stoppingLoops.Add(slot);
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

        private void CancelLoopWatcher(string slot) => CancelWatcher(_loopWatchers, slot);

        private static void CancelWatcher(Dictionary<string, CancellationTokenSource> watchers, string slot)
        {
            if (watchers.Remove(slot, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        public void SetLoopVolume(string slot, float volumeScale, float duration = 0f)
        {
            if (!TryGetLiveLoop(slot, out var source)) return;

            CancelLoopWatcher(slot);
            if (duration > 0f)
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                _loopWatchers[slot] = cts;
                RampAsync(source, pitch: false, volumeScale, duration, cts.Token).Forget();
            }
            else
            {
                source.volume = volumeScale;
            }
        }

        public void SetLoopPitch(string slot, float pitch, float duration = 0f)
        {
            if (!TryGetLiveLoop(slot, out var source)) return;

            CancelWatcher(_loopPitchWatchers, slot);
            if (duration > 0f)
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                _loopPitchWatchers[slot] = cts;
                RampAsync(source, pitch: true, pitch, duration, cts.Token).Forget();
            }
            else
            {
                source.pitch = pitch;
            }
        }

        private bool TryGetLiveLoop(string slot, out AudioSource source)
        {
            source = null!;
            if (string.IsNullOrEmpty(slot) || _stoppingLoops.Contains(slot)) return false;
            if (!_loopSources.TryGetValue(slot, out var found) || found == null || found.resource == null) return false;
            source = found;
            return true;
        }

        private async UniTaskVoid RampAsync(AudioSource source, bool pitch, float target, float duration, CancellationToken ct)
        {
            try
            {
                float start = pitch ? source.pitch : source.volume;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += DeltaTime;
                    float value = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
                    if (source == null) return;
                    if (pitch) source.pitch = value;
                    else source.volume = value;
                    await UniTask.Yield(ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Debug.LogException(ex); return; }

            if (source == null) return;
            if (pitch) source.pitch = target;
            else source.volume = target;
        }

        private async UniTaskVoid FadeOutLoopAsync(string slot, AudioSource source, float duration, CancellationToken ct)
        {
            try
            {
                float startVolume = source != null ? source.volume : 0f;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += DeltaTime;
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
            _stoppingLoops.Remove(slot);
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
            Apply3DSettings(source);
            return source;
        }

        public void StopSound(SoundHandle handle, float fadeOut = 0f)
        {
            if (!handle.IsValid) return;
            if (!_handleSources.TryGetValue(handle.Id, out var source)) return;

            if (fadeOut > 0f)
            {
                Unlink(source);
                EndWatch(source);
                UntrackHandle(source);
                FadeOutAndReclaimAsync(source, fadeOut, _cts.Token).Forget();
            }
            else
            {
                ReturnSource(source);
            }
        }

        public void StopAllSFX(float fadeOut = 0f)
        {
            // StopSound unlinks sources from the active list, so walk a copy.
            _stopBuffer.Clear();
            foreach (var source in _activeSources)
                _stopBuffer.Add(source);

            foreach (var source in _stopBuffer)
            {
                if (_sourceHandles.TryGetValue(source, out var id))
                    StopSound(new SoundHandle(id), fadeOut);
                else
                    ReturnSource(source);
            }
            _stopBuffer.Clear();
        }

        private async UniTaskVoid FadeOutAndReclaimAsync(AudioSource source, float duration, CancellationToken ct)
        {
            try
            {
                float startVolume = source != null ? source.volume : 0f;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += DeltaTime;
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

        private async UniTaskVoid CrossfadeAsync(
            AudioSource outgoing, AudioSource incoming,
            float outgoingStartVolume, float duration, CancellationToken ct)
        {
            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += DeltaTime;
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

        public void SetVolume(string mixerParam, float volume01)
        {
            if (string.IsNullOrEmpty(mixerParam)) return;

            float volume = Mathf.Clamp01(volume01);
            _volumes[mixerParam] = volume;
            ApplyVolume(mixerParam, volume);
        }

        public float GetVolume(string mixerParam) =>
            !string.IsNullOrEmpty(mixerParam) && _volumes.TryGetValue(mixerParam, out var volume) ? volume : 1f;

        private void ApplyVolume(string param, float volume01)
        {
            if (_mixer == null || string.IsNullOrEmpty(param)) return;
            float dB = volume01 > 0.0001f ? Mathf.Log10(volume01) * 20f : -80f;
            if (!_mixer.SetFloat(param, dB))
                Debug.LogWarning($"[AudioService] Mixer parameter '{param}' is not exposed.");
        }

        public void Dispose()
        {
            _crossfadeCts?.Cancel();
            _crossfadeCts?.Dispose();

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

            foreach (var kvp in _loopPitchWatchers)
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }
            _loopPitchWatchers.Clear();

            _cts.Cancel();
            _cts.Dispose();
            if (_root != null)
            {
                if (Application.isPlaying) Object.Destroy(_root);
                else Object.DestroyImmediate(_root);
            }
        }

        private readonly struct Voice
        {
            public readonly int Priority;
            public readonly AudioResource Resource;

            public Voice(int priority, AudioResource resource)
            {
                Priority = priority;
                Resource = resource;
            }
        }
    }
}
