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
    /// no volume until asked; saving and restoring volumes is the caller's job. The pool never grows past
    /// <see cref="AudioServiceConfig.MaxSfxSources"/>: a full pool first takes the quietest one-shot fading out, then
    /// evicts the lowest priority one, oldest first; <see cref="SoundConfig.MaxInstances"/> and
    /// <see cref="SoundConfig.MinInterval"/> drop repeats of one sound before they reach the pool. A loop slot plays
    /// in 2D, at a point or following a transform; a destroyed transform fades its loop out.
    /// </summary>
    public class UnityAudioService : IAudioService, IDisposable
    {
        private readonly AudioMixer _mixer;
        private readonly AudioMixerGroup _musicGroup = default!;
        private readonly AudioMixerGroup _sfxGroup = default!;
        private readonly GameObject _root;
        // Three, so a switch in the middle of a crossfade lets both tracks it heard fade out.
        private readonly AudioSource[] _musicSources = new AudioSource[3];
        private readonly float[] _musicFadeFrom = new float[3];
        private readonly float _crossfadeDuration;
        private readonly bool _unscaledTime;
        private readonly AudioRolloffMode _rolloff;
        private readonly float _minDistance;
        private readonly float _maxDistance;
        private readonly float _dopplerLevel;
        private readonly float _lostTargetFadeOut;
        private readonly Queue<AudioSource> _sfxPool = new();
        private readonly LinkedList<AudioSource> _activeSources = new();
        // Stopped with a fade: out of the active list, not yet back in the pool.
        private readonly List<AudioSource> _fadingSources = new();
        private readonly Dictionary<AudioSource, LinkedListNode<AudioSource>> _activeNodes = new();
        private readonly Dictionary<AudioSource, Voice> _voices = new();
        private readonly Dictionary<AudioResource, int> _instanceCounts = new();
        private readonly Dictionary<AudioResource, double> _lastStarts = new();
        private readonly Dictionary<long, AudioSource> _handleSources = new();
        private readonly Dictionary<AudioSource, long> _sourceHandles = new();
        private readonly Dictionary<AudioSource, CancellationTokenSource> _sourceWatchers = new();
        private readonly Dictionary<string, AudioSource> _loopSources = new();
        // Sources of stopped slots, for the next slot to start; a level's worth of attached loops is not leaked.
        private readonly Stack<AudioSource> _freeLoopSources = new();
        private readonly Dictionary<string, CancellationTokenSource> _loopWatchers = new();
        private readonly Dictionary<string, CancellationTokenSource> _loopPitchWatchers = new();
        private readonly HashSet<string> _stoppingLoops = new();
        private readonly Dictionary<string, Transform> _loopFollows = new();
        private readonly List<string> _lostFollows = new();
        private readonly List<AudioSource> _stopBuffer = new();
        private readonly Dictionary<string, float> _volumes = new();
        private readonly CancellationTokenSource _cts = new();

        // Clock for MinInterval, the same time the fades run on.
        private readonly Func<double> _now;

        private long _nextHandleId = 1;
        private int _currentMusic = -1;
        private bool _followingLoops;
        private CancellationTokenSource? _crossfadeCts;

        // Silence on the mixer, the floor of its volume faders.
        private const float MinDecibels = -80f;

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
            _lostTargetFadeOut = config.LostTargetFadeOut;
            _now = _unscaledTime ? () => Time.realtimeSinceStartupAsDouble : () => Time.timeAsDouble;

            _root = new GameObject("[AudioService]");
            if (Application.isPlaying)
                Object.DontDestroyOnLoad(_root);

            for (int i = 0; i < _musicSources.Length; i++)
                _musicSources[i] = CreateMusicSource($"Music_{(char)('A' + i)}");

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
            source.volume = 0f;
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
        private AudioSource? RentSource(in SoundConfig sound, Transform? follow)
        {
            if (!sound.IsValid || !PassesRepeatLimit(in sound)) return null;

            AudioSource source;

            if (_sfxPool.Count > 0)
            {
                source = _sfxPool.Dequeue();
            }
            else if (_fadingSources.Count > 0)
            {
                // A stopped sound is on its way out anyway: cutting the quietest one costs least.
                source = TakeQuietestFading();
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
                // Every source is somewhere else: only a source destroyed from outside gets here.
                return null;
            }

            var node = _activeSources.AddLast(source);
            _activeNodes[source] = node;
            _voices[source] = new Voice(sound.Priority, sound.Resource, follow);
            _instanceCounts[sound.Resource] = InstanceCount(sound.Resource) + 1;
            _lastStarts[sound.Resource] = _now();
            return source;
        }

        private AudioSource TakeQuietestFading()
        {
            int quietest = 0;
            for (int i = 1; i < _fadingSources.Count; i++)
                if (_fadingSources[i].volume < _fadingSources[quietest].volume) quietest = i;

            var source = _fadingSources[quietest];
            _fadingSources.RemoveAt(quietest);
            EndWatch(source);
            source.Stop();
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
            source.ignoreListenerPause = false;
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

        // AudioListener.pause makes isPlaying false without stopping the sound, which resumes where it was:
        // a paused source is still busy. A free or stopped source has no resource.
        private static bool IsSounding(AudioSource source) =>
            source.isPlaying || (AudioListener.pause && !source.ignoreListenerPause && source.resource != null);

        // Whether a one-shot is still playing. Unity keeps isPlaying true on a source that plays a container
        // (AudioRandomContainer) through AudioListener.pause until the pause ends, long after the sound is over;
        // such a one-shot is over once its output has stayed silent for SilenceEnds.
        private sealed class OneShotEnd
        {
            private const double SilenceEnds = 0.2;
            private const float Floor = 1e-5f;

            // Main thread only, like the whole service.
            private static readonly float[] Output = new float[256];

            private double _heardAt = double.NaN;

            public bool Sounding(AudioSource source)
            {
                if (!IsSounding(source)) return false;

                if (!AudioListener.pause || !source.ignoreListenerPause || source.resource is AudioClip)
                {
                    _heardAt = double.NaN;
                    return true;
                }

                double now = Time.realtimeSinceStartupAsDouble;
                if (double.IsNaN(_heardAt) || Heard(source)) _heardAt = now;
                return now - _heardAt < SilenceEnds;
            }

            private static bool Heard(AudioSource source)
            {
                source.GetOutputData(Output, 0);
                foreach (float sample in Output)
                    if (sample > Floor || sample < -Floor) return true;
                return false;
            }
        }

        private async UniTaskVoid ReturnAfterPlayAsync(AudioSource source, CancellationToken ct)
        {
            try
            {
                var end = new OneShotEnd();
                await UniTask.WaitWhile(() => source != null && end.Sounding(source),
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

        // Where the sound goes and whether it goes on through AudioListener.pause.
        private void ApplyRouting(AudioSource source, in SoundConfig sound)
        {
            source.outputAudioMixerGroup = sound.Output != null ? sound.Output : _sfxGroup;
            source.ignoreListenerPause = sound.PlaysOnPause;
        }

        private static void ApplyPitch(AudioSource source, in SoundConfig sound)
        {
            float variation = sound.PitchVariation;
            source.pitch = variation > 0f ? 1f + UnityEngine.Random.Range(-variation, variation) : 1f;
        }

        public SoundHandle PlaySFX(in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f)
        {
            var source = RentSource(in sound, follow: null);
            if (source == null) return SoundHandle.Invalid;

            source.spatialBlend = 0f;
            return Start(source, in sound, volumeScale, fadeIn, follow: null);
        }

        public SoundHandle PlaySFXAtPoint(in SoundConfig sound, Vector3 position, float volumeScale = 1f, float fadeIn = 0f)
        {
            var source = RentSource(in sound, follow: null);
            if (source == null) return SoundHandle.Invalid;

            source.transform.position = position;
            source.spatialBlend = 1f;
            return Start(source, in sound, volumeScale, fadeIn, follow: null);
        }

        public SoundHandle PlaySFXAttached(in SoundConfig sound, Transform follow, float volumeScale = 1f, float fadeIn = 0f)
        {
            if (follow == null) return SoundHandle.Invalid;
            var source = RentSource(in sound, follow);
            if (source == null) return SoundHandle.Invalid;

            source.transform.position = follow.position;
            source.spatialBlend = 1f;
            return Start(source, in sound, volumeScale, fadeIn, follow);
        }

        private SoundHandle Start(AudioSource source, in SoundConfig sound, float volumeScale, float fadeIn, Transform? follow)
        {
            source.resource = sound.Resource;
            ApplyRouting(source, in sound);
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
                var end = new OneShotEnd();
                while (source != null && end.Sounding(source))
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
            if (string.IsNullOrEmpty(slot) || !sound.IsValid) return;

            _loopFollows.Remove(slot);
            StartLoop(slot, in sound, volumeScale, fadeIn, spatialBlend: 0f);
        }

        public void PlayLoopAtPoint(string slot, in SoundConfig sound, Vector3 position, float volumeScale = 1f, float fadeIn = 0f)
        {
            if (string.IsNullOrEmpty(slot) || !sound.IsValid) return;

            _loopFollows.Remove(slot);
            StartLoop(slot, in sound, volumeScale, fadeIn, spatialBlend: 1f).transform.position = position;
        }

        public void PlayLoopAttached(string slot, in SoundConfig sound, Transform follow, float volumeScale = 1f, float fadeIn = 0f)
        {
            if (string.IsNullOrEmpty(slot) || !sound.IsValid || follow == null) return;

            StartLoop(slot, in sound, volumeScale, fadeIn, spatialBlend: 1f).transform.position = follow.position;
            _loopFollows[slot] = follow;
            if (!_followingLoops) FollowLoopsAsync(_cts.Token).Forget();
        }

        private AudioSource StartLoop(string slot, in SoundConfig sound, float volumeScale, float fadeIn, float spatialBlend)
        {
            CancelLoopWatcher(slot);
            CancelWatcher(_loopPitchWatchers, slot);
            _stoppingLoops.Remove(slot);

            if (!_loopSources.TryGetValue(slot, out var source))
            {
                source = RentLoopSource(slot);
                _loopSources[slot] = source;
            }

            source.Stop();
            source.spatialBlend = spatialBlend;
            source.resource = sound.Resource;
            source.loop = true;
            ApplyRouting(source, in sound);
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
            return source;
        }

        // One runner for all attached slots; it ends when the last one is gone.
        private async UniTaskVoid FollowLoopsAsync(CancellationToken ct)
        {
            _followingLoops = true;
            try
            {
                while (_loopFollows.Count > 0)
                {
                    await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, ct);
                    StepLoopFollows();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.LogException(ex); }
            finally { _followingLoops = false; }
        }

        private void StepLoopFollows()
        {
            _lostFollows.Clear();
            foreach (var (slot, follow) in _loopFollows)
            {
                if (follow == null) _lostFollows.Add(slot);
                else if (_loopSources.TryGetValue(slot, out var source) && source != null)
                    source.transform.position = follow.position;
            }

            foreach (var slot in _lostFollows)
            {
                _loopFollows.Remove(slot);
                // The loop fades where the object was last seen; a loop already fading out keeps its own fade.
                if (!_stoppingLoops.Contains(slot)) StopLoop(slot, _lostTargetFadeOut);
            }
            _lostFollows.Clear();
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
                FreeLoop(slot, source);
            }
        }

        public bool IsLoopPlaying(string slot)
        {
            if (string.IsNullOrEmpty(slot)) return false;
            return _loopSources.TryGetValue(slot, out var source) && source != null && IsSounding(source);
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

            if (_loopWatchers.TryGetValue(slot, out var stored) && !stored.Token.IsCancellationRequested)
            {
                _loopWatchers.Remove(slot);
                stored.Dispose();
            }
            if (_loopSources.TryGetValue(slot, out var current) && current == source)
                FreeLoop(slot, source);
        }

        // The slot is gone once its loop stops; its source waits for the next slot to start.
        private void FreeLoop(string slot, AudioSource source)
        {
            CancelLoopWatcher(slot);
            CancelWatcher(_loopPitchWatchers, slot);
            _stoppingLoops.Remove(slot);
            _loopFollows.Remove(slot);
            _loopSources.Remove(slot);
            if (source == null) return;

            source.Stop();
            source.resource = null;
            _freeLoopSources.Push(source);
        }

        private AudioSource RentLoopSource(string slot)
        {
            while (_freeLoopSources.Count > 0)
            {
                var free = _freeLoopSources.Pop();
                if (free == null) continue;
                free.gameObject.name = $"Loop_{slot}";
                return free;
            }
            return CreateLoopSource(slot);
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
                // An attached sound keeps following while it fades.
                var follow = _voices.TryGetValue(source, out var voice) ? voice.Follow : null;
                Unlink(source);
                EndWatch(source);
                UntrackHandle(source);
                _fadingSources.Add(source);
                FadeOutAndReclaimAsync(source, fadeOut, follow, BeginWatch(source)).Forget();
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

        // Cancelled when the pool takes the source before the fade ends.
        private async UniTaskVoid FadeOutAndReclaimAsync(AudioSource source, float duration, Transform? follow,
            CancellationToken ct)
        {
            // A follower moves after the target has, like FollowAndReturnAsync.
            var timing = follow != null ? PlayerLoopTiming.LastPostLateUpdate : PlayerLoopTiming.Update;
            try
            {
                float startVolume = source != null ? source.volume : 0f;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += DeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    if (source != null)
                    {
                        source.volume = startVolume * (1f - t);
                        if (follow != null) source.transform.position = follow.position;
                    }
                    await UniTask.Yield(timing, ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Debug.LogException(ex); return; }

            _fadingSources.Remove(source);
            EndWatch(source);
            if (source != null)
                ReclaimToPool(source);
        }

        // A track already playing, or fading in or out, goes on from where it is; every other audible track fades out
        // from its current volume.
        public void PlayMusic(in MusicConfig music, float? crossfadeDuration = null)
        {
            if (!music.IsValid) return;

            CancelCrossfade();
            float duration = crossfadeDuration ?? _crossfadeDuration;

            int incoming = MusicPlaying(music.Resource);
            if (incoming < 0)
            {
                incoming = FreeMusicSource();
                var source = _musicSources[incoming];
                source.Stop();
                source.resource = music.Resource;
                source.pitch = 1f;
                source.volume = 0f;
                source.Play();
            }
            _musicSources[incoming].ignoreListenerPause = music.PlaysOnPause;
            _currentMusic = incoming;

            if (duration > 0f && OtherMusicAudible(incoming))
            {
                for (int i = 0; i < _musicSources.Length; i++)
                    _musicFadeFrom[i] = _musicSources[i].volume;
                _crossfadeCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                CrossfadeAsync(incoming, duration, _crossfadeCts.Token).Forget();
            }
            else
            {
                SettleMusic(incoming);
            }
        }

        // The source already playing this track, the current one first.
        private int MusicPlaying(AudioResource resource)
        {
            if (_currentMusic >= 0 && PlaysMusic(_musicSources[_currentMusic], resource)) return _currentMusic;
            for (int i = 0; i < _musicSources.Length; i++)
                if (PlaysMusic(_musicSources[i], resource)) return i;
            return -1;
        }

        private static bool PlaysMusic(AudioSource source, AudioResource resource) =>
            source.resource == resource && IsSounding(source);

        // A silent source if there is one; otherwise the quietest track fading out gives way.
        private int FreeMusicSource()
        {
            int quietest = -1;
            for (int i = 0; i < _musicSources.Length; i++)
            {
                if (i == _currentMusic) continue;
                var source = _musicSources[i];
                if (!IsSounding(source)) return i;
                if (quietest < 0 || source.volume < _musicSources[quietest].volume) quietest = i;
            }
            return quietest;
        }

        private bool OtherMusicAudible(int current)
        {
            for (int i = 0; i < _musicSources.Length; i++)
                if (i != current && IsSounding(_musicSources[i]) && _musicSources[i].volume > 0f) return true;
            return false;
        }

        // The current track at full volume, every other one stopped.
        private void SettleMusic(int current)
        {
            for (int i = 0; i < _musicSources.Length; i++)
            {
                var source = _musicSources[i];
                if (source == null) continue;
                if (i == current)
                {
                    source.volume = 1f;
                    continue;
                }
                source.Stop();
                source.resource = null;
                source.volume = 0f;
            }
        }

        private void CancelCrossfade()
        {
            _crossfadeCts?.Cancel();
            _crossfadeCts?.Dispose();
            _crossfadeCts = null;
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

        // Every source moves from its volume at the start to full (the current one) or silence over the duration.
        private async UniTaskVoid CrossfadeAsync(int current, float duration, CancellationToken ct)
        {
            try
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += DeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    for (int i = 0; i < _musicSources.Length; i++)
                    {
                        var source = _musicSources[i];
                        if (source != null) source.volume = Mathf.Lerp(_musicFadeFrom[i], i == current ? 1f : 0f, t);
                    }
                    await UniTask.Yield(ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return;
            }

            SettleMusic(current);
        }

        public void StopMusic()
        {
            CancelCrossfade();

            foreach (var source in _musicSources)
            {
                source.Stop();
                source.resource = null;
                source.volume = 0f;
            }

            _currentMusic = -1;
        }

        public void SetVolume(string mixerParam, float volume01)
        {
            if (string.IsNullOrEmpty(mixerParam)) return;

            float volume = Mathf.Clamp01(volume01);
            _volumes[mixerParam] = volume;
            ApplyVolume(mixerParam, volume);
        }

        public float GetVolume(string mixerParam)
        {
            if (string.IsNullOrEmpty(mixerParam)) return 1f;
            if (_volumes.TryGetValue(mixerParam, out var volume)) return volume;
            // Never set here: the mixer's own value, from its asset or a snapshot.
            if (_mixer == null || !_mixer.GetFloat(mixerParam, out float dB)) return 1f;
            return dB > MinDecibels ? Mathf.Clamp01(Mathf.Pow(10f, dB / 20f)) : 0f;
        }

        private void ApplyVolume(string param, float volume01)
        {
            if (_mixer == null || string.IsNullOrEmpty(param)) return;
            float dB = volume01 > 0.0001f ? Mathf.Log10(volume01) * 20f : MinDecibels;
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
            _loopFollows.Clear();

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
            public readonly Transform? Follow;

            public Voice(int priority, AudioResource resource, Transform? follow)
            {
                Priority = priority;
                Resource = resource;
                Follow = follow;
            }
        }
    }
}
