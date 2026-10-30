using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Rubickanov.Log;
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
    /// in 2D, at a point or following a transform; a destroyed transform fades its loop out. A <see cref="SoundCue"/>
    /// plays through the same pool and loop machinery: its variant is picked here, its limits count over all its
    /// variants, and the pool ranks it by <see cref="SoundCue.Priority"/> on the <see cref="SoundPriority"/> scale.
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
        private readonly SoundSpace? _space;
        private readonly OutputLimiterSettings _limiterSettings;
        private AudioListener? _limitedEars;
        private OutputLimiter? _limiter;
        private bool _limiterAdded;
        private bool _limiterOn;
        private float _nextEarsFind;
        // Sources playing a kind: their volume is owned by the space (it carries head shadow and occlusion).
        private readonly Dictionary<AudioSource, SpatialHandle> _spatial = new();
        private readonly Dictionary<AudioSource, Adopted> _adopted = new();

        // Cues: pick state and counters, and the loops their handles own (under a private slot key).
        private readonly Dictionary<SoundCue, CueState> _cues = new();
        private readonly Dictionary<long, CueLoop> _cueLoops = new();
        private readonly Dictionary<string, long> _loopIds = new();
        private ICueRandom _random = UnityCueRandom.Shared;
        private bool _logPlays;
        private static readonly LogChannel Log = LogChannel.Get("Audio");

        // Clock for MinInterval, the same time the fades run on.
        private readonly Func<double> _now;

        private long _nextHandleId = 1;
        private int _currentMusic = -1;
        private bool _followingLoops;
        private CancellationTokenSource? _crossfadeCts;

        // Silence on the mixer, the floor of its volume faders.
        private const float MinDecibels = -80f;
        // Unity's own default AudioSource.priority: what the SoundConfig path always plays with in 2D.
        private const int UnityDefaultPriority = 128;

        /// <param name="space">Applies <see cref="SoundConfig.Kind"/> to point and attached plays; null: kinds are ignored.</param>
        public UnityAudioService(AudioServiceConfig config, SoundSpace? space = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            _space = space;

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
            _limiterSettings = config.Limiter ?? new OutputLimiterSettings();
            _limiterOn = _limiterSettings.Enabled;
            _now = _unscaledTime ? () => Time.realtimeSinceStartupAsDouble : () => Time.timeAsDouble;

            _root = new GameObject("[AudioService]");
            if (Application.isPlaying)
                Object.DontDestroyOnLoad(_root);

            for (int i = 0; i < _musicSources.Length; i++)
                _musicSources[i] = CreateMusicSource($"Music_{(char)('A' + i)}");

            int maxSources = Mathf.Max(1, config.MaxSfxSources);
            for (int i = 0; i < maxSources; i++)
                _sfxPool.Enqueue(CreateSFXSource());

            if (_space != null) TickSpaceAsync(_space, _cts.Token).Forget();
            if (_limiterSettings.Enabled && Application.isPlaying) KeepLimiterAsync(_cts.Token).Forget();
        }

        /// <summary>The limiter on the active listener's output; null while nothing listens or it is off.</summary>
        public OutputLimiter? Limiter => _limiter != null ? _limiter : null;

        /// <summary>
        /// The output limiter on (an A/B switch that also holds for listeners found later); false when the config
        /// turns it off.
        /// </summary>
        public bool LimiterOn
        {
            get => _limiterOn;
            set
            {
                _limiterOn = value && _limiterSettings.Enabled;
                if (_limiter != null) _limiter.On = _limiterOn;
            }
        }

        // The active listener carries the output limiter; a listener swap (camera, a probe's ears) moves it along.
        private async UniTaskVoid KeepLimiterAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                    if (_limitedEars != null && _limitedEars.isActiveAndEnabled && _limiter != null) continue;
                    float now = Time.realtimeSinceStartup;
                    if (now < _nextEarsFind && _limitedEars == null) continue;
                    _nextEarsFind = now + 1f;
                    _limitedEars = null;
                    foreach (AudioListener l in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                    {
                        if (!l.isActiveAndEnabled) continue;
                        _limitedEars = l;
                        break;
                    }
                    if (_limitedEars == null) continue;
                    _limiterAdded = !_limitedEars.TryGetComponent(out _limiter);
                    if (_limiterAdded) _limiter = _limitedEars.gameObject.AddComponent<OutputLimiter>();
                    _limiter!.Configure(_limiterSettings);
                    _limiter.On = _limiterOn;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        /// <summary>The space kinds play through; null when the service was made without one.</summary>
        public SoundSpace? Space => _space;

        // After the camera has moved; a game that ticks the space itself sets TicksItself false.
        private static async UniTaskVoid TickSpaceAsync(SoundSpace space, CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
                    if (space.TicksItself) space.Tick(Time.unscaledDeltaTime);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.LogException(ex); }
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
            source.bypassReverbZones = true;
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
            PrepareSpatial(source);
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

        // A kind-less sound never sends to a reverb zone. With a space the low-pass is added now, disabled, so a
        // play only enables it instead of rebuilding the DSP chain of a source about to start.
        private void PrepareSpatial(AudioSource source)
        {
            source.bypassReverbZones = true;
            if (_space == null) return;
            var filter = source.gameObject.AddComponent<AudioLowPassFilter>();
            filter.enabled = false;
        }

        // Registers a 3D play of a sound with a kind; its output: the sound's, the kind's, the SFX group.
        private void Spatialize(AudioSource source, in Spec spec, float volume)
        {
            var kind = spec.Kind;
            if (_space == null || kind == null) return;
            source.outputAudioMixerGroup = spec.SpatialOutput;
            _spatial[source] = _space.Add(source, kind, volume);
        }

        // Unity's voice priority after the kind's Shape: a cue's from its scale; the SoundConfig path keeps the kind's
        // in 3D and Unity's default otherwise, as a pooled source may hold someone else's.
        private static void ApplyPriority(AudioSource source, in Spec spec, bool shaped)
        {
            if (spec.UnityPriority >= 0) source.priority = spec.UnityPriority;
            else if (!shaped) source.priority = UnityDefaultPriority;
        }

        // Back to the config's 3D settings and no reverb send.
        private void Unspatialize(AudioSource source)
        {
            if (_space == null || !_spatial.Remove(source, out var handle)) return;
            _space.Remove(handle);
            SoundSpace.Unshape(source);
            Apply3DSettings(source);
        }

        private float VolumeOf(AudioSource source) =>
            _space != null && _spatial.TryGetValue(source, out var handle) ? _space.VolumeOf(handle) : source.volume;

        private void SetVolumeOf(AudioSource source, float volume)
        {
            if (_space != null && _spatial.TryGetValue(source, out var handle)) _space.SetVolume(handle, volume);
            else source.volume = volume;
        }

        // Null when the sound may not play: invalid, over its repeat limit, or below every sound in a full pool.
        private AudioSource? RentSource(in SoundConfig sound, Transform? follow)
        {
            if (!sound.IsValid || !PassesRepeatLimit(in sound)) return null;

            // The old API on the cue scale: one order for both.
            int rank = SoundPriority.Default + sound.Priority;
            var source = TakeSource(rank);
            if (source == null) return null;

            Admit(source, new Voice(rank, sound.Resource, null, follow));
            _instanceCounts[sound.Resource] = InstanceCount(sound.Resource) + 1;
            _lastStarts[sound.Resource] = _now();
            return source;
        }

        // Null when the cue may not play: none, no variants, over its limits, or below every sound in a full pool.
        private AudioSource? RentCue(SoundCue? cue, int variant, Transform? follow, out AudioResource resource)
        {
            resource = null!;
            if (cue == null || cue.Variants == null || cue.Variants.Length == 0) return null;

            _cues.TryGetValue(cue, out var state);
            AudioSource? source = null;
            if (PassesCueLimits(cue, in state))
            {
                resource = cue.Variants[CuePick.Next(cue, ref state, variant, _random)];
                if (resource == null)
                {
                    _cues[cue] = state;
                    return null;
                }
                source = TakeSource(cue.Priority);
            }

            if (source == null) state.Dropped++;
            else
            {
                Admit(source, new Voice(cue.Priority, resource, cue, follow));
                CountStart(ref state);
            }
            _cues[cue] = state;
            return source;
        }

        private bool PassesCueLimits(SoundCue cue, in CueState state)
        {
            if (cue.MaxInstances > 0 && state.Playing >= cue.MaxInstances) return false;
            if (cue.MinInterval > 0f && state.Started && _now() - state.LastStart < cue.MinInterval) return false;
            return true;
        }

        private void CountStart(ref CueState state)
        {
            state.Playing++;
            state.Played++;
            state.Started = true;
            state.LastStart = _now();
        }

        private void CountEnd(SoundCue cue)
        {
            if (!_cues.TryGetValue(cue, out var state) || state.Playing <= 0) return;
            state.Playing--;
            _cues[cue] = state;
        }

        // A free source for a sound of this rank; null when every playing sound outranks it.
        private AudioSource? TakeSource(int rank)
        {
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
                if (_voices[victim].Priority > rank) return null;

                source = victim;
                Unlink(source);
                EndWatch(source);
                UntrackHandle(source);
                source.Stop();
                Unspatialize(source);
            }
            else
            {
                // Every source is somewhere else: only a source destroyed from outside gets here.
                return null;
            }
            return source;
        }

        private void Admit(AudioSource source, in Voice voice)
        {
            _activeNodes[source] = _activeSources.AddLast(source);
            _voices[source] = voice;
        }

        private AudioSource TakeQuietestFading()
        {
            int quietest = 0;
            for (int i = 1; i < _fadingSources.Count; i++)
                if (VolumeOf(_fadingSources[i]) < VolumeOf(_fadingSources[quietest])) quietest = i;

            var source = _fadingSources[quietest];
            _fadingSources.RemoveAt(quietest);
            EndWatch(source);
            source.Stop();
            Unspatialize(source);
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
                if (voice.Cue != null)
                {
                    CountEnd(voice.Cue);
                    return;
                }
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
            Unspatialize(source);
            source.bypassReverbZones = true;
            source.resource = null;
            source.spatialBlend = 0f;
            source.loop = false;
            source.pitch = 1f;
            source.volume = 1f;
            source.priority = UnityDefaultPriority;
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
        private static void ApplyRouting(AudioSource source, in Spec spec)
        {
            source.outputAudioMixerGroup = spec.Output;
            source.ignoreListenerPause = spec.PlaysOnPause;
        }

        private static void ApplyPitch(AudioSource source, in Spec spec)
        {
            float variation = spec.PitchJitter;
            source.pitch = variation > 0f ? 1f + UnityEngine.Random.Range(-variation, variation) : 1f;
        }

        private Spec SpecOf(in SoundConfig sound)
        {
            var output = sound.Output != null ? sound.Output : _sfxGroup;
            var kind = sound.Kind;
            var spatial = sound.Output != null ? sound.Output : kind != null && kind.Output != null ? kind.Output : _sfxGroup;
            return new Spec(sound.Resource, output, spatial, kind, sound.PitchVariation, sound.PlaysOnPause, -1);
        }

        private Spec SpecOf(SoundCue cue, AudioResource resource)
        {
            var group = GroupOf(cue);
            return new Spec(resource, group, group, cue.Kind, cue.PitchJitter, cue.PlaysOnPause,
                SoundPriority.ToUnity(cue.Priority));
        }

        // cue.Volume quieter by a random 0..VolumeJitter dB.
        private static float LevelOf(SoundCue cue)
        {
            float jitter = cue.VolumeJitter;
            float db = jitter > 0f ? -UnityEngine.Random.Range(0f, jitter) : 0f;
            return cue.Volume * SoundMath.DbToGain(db);
        }

        public SoundHandle PlaySFX(in SoundConfig sound, float volumeScale = 1f, float fadeIn = 0f)
        {
            var source = RentSource(in sound, follow: null);
            if (source == null) return SoundHandle.Invalid;

            source.spatialBlend = 0f;
            var spec = SpecOf(in sound);
            return Start(source, in spec, volumeScale, fadeIn, follow: null, spatial: false);
        }

        public SoundHandle PlaySFXAtPoint(in SoundConfig sound, Vector3 position, float volumeScale = 1f, float fadeIn = 0f)
        {
            var source = RentSource(in sound, follow: null);
            if (source == null) return SoundHandle.Invalid;

            source.transform.position = position;
            source.spatialBlend = 1f;
            var spec = SpecOf(in sound);
            return Start(source, in spec, volumeScale, fadeIn, follow: null, spatial: true);
        }

        public SoundHandle PlaySFXAttached(in SoundConfig sound, Transform follow, float volumeScale = 1f, float fadeIn = 0f)
        {
            if (follow == null) return SoundHandle.Invalid;
            var source = RentSource(in sound, follow);
            if (source == null) return SoundHandle.Invalid;

            source.transform.position = follow.position;
            source.spatialBlend = 1f;
            var spec = SpecOf(in sound);
            return Start(source, in spec, volumeScale, fadeIn, follow, spatial: true);
        }

        private SoundHandle Start(AudioSource source, in Spec spec, float volumeScale, float fadeIn, Transform? follow,
            bool spatial)
        {
            source.resource = spec.Resource;
            ApplyRouting(source, in spec);
            ApplyPitch(source, in spec);
            if (spatial) Spatialize(source, in spec, fadeIn > 0f ? 0f : volumeScale);
            ApplyPriority(source, in spec, _spatial.ContainsKey(source));
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
                SetVolumeOf(source, 0f);
                source.Play();
                // Tie the fade to the per-source watcher token, not the service-lifetime _cts:
                // if the source is returned, evicted, or stopped mid-fade, EndWatch cancels this
                // token so the fade stops writing volume to what is now a recycled source playing
                // a different sound.
                FadeInAsync(source, targetVolume, fadeIn, watch).Forget();
            }
            else
            {
                SetVolumeOf(source, targetVolume);
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
                    if (source != null) SetVolumeOf(source, targetVolume * t);
                    await UniTask.Yield(ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Debug.LogException(ex); return; }

            if (source != null) SetVolumeOf(source, targetVolume);
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
            var spec = SpecOf(in sound);
            StartLoop(slot, in spec, volumeScale, fadeIn, spatialBlend: 0f);
        }

        public void PlayLoopAtPoint(string slot, in SoundConfig sound, Vector3 position, float volumeScale = 1f, float fadeIn = 0f)
        {
            if (string.IsNullOrEmpty(slot) || !sound.IsValid) return;

            _loopFollows.Remove(slot);
            var spec = SpecOf(in sound);
            StartLoop(slot, in spec, volumeScale, fadeIn, spatialBlend: 1f).transform.position = position;
        }

        public void PlayLoopAttached(string slot, in SoundConfig sound, Transform follow, float volumeScale = 1f, float fadeIn = 0f)
        {
            if (string.IsNullOrEmpty(slot) || !sound.IsValid || follow == null) return;

            var spec = SpecOf(in sound);
            StartLoop(slot, in spec, volumeScale, fadeIn, spatialBlend: 1f).transform.position = follow.position;
            _loopFollows[slot] = follow;
            if (!_followingLoops) FollowLoopsAsync(_cts.Token).Forget();
        }

        private AudioSource StartLoop(string slot, in Spec spec, float volumeScale, float fadeIn, float spatialBlend)
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
            Unspatialize(source);
            source.spatialBlend = spatialBlend;
            source.resource = spec.Resource;
            source.loop = true;
            ApplyRouting(source, in spec);
            ApplyPitch(source, in spec);
            if (spatialBlend > 0f) Spatialize(source, in spec, fadeIn > 0f ? 0f : volumeScale);
            ApplyPriority(source, in spec, _spatial.ContainsKey(source));

            if (fadeIn > 0f)
            {
                SetVolumeOf(source, 0f);
                source.Play();
                var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                _loopWatchers[slot] = cts;
                FadeInAsync(source, volumeScale, fadeIn, cts.Token).Forget();
            }
            else
            {
                SetVolumeOf(source, volumeScale);
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
                SetVolumeOf(source, volumeScale);
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
                float start = pitch ? source.pitch : VolumeOf(source);
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += DeltaTime;
                    float value = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
                    if (source == null) return;
                    if (pitch) source.pitch = value;
                    else SetVolumeOf(source, value);
                    await UniTask.Yield(ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Debug.LogException(ex); return; }

            if (source == null) return;
            if (pitch) source.pitch = target;
            else SetVolumeOf(source, target);
        }

        private async UniTaskVoid FadeOutLoopAsync(string slot, AudioSource source, float duration, CancellationToken ct)
        {
            try
            {
                float startVolume = source != null ? VolumeOf(source) : 0f;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += DeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    if (source != null) SetVolumeOf(source, startVolume * (1f - t));
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
            if (_loopIds.Remove(slot, out var id) && _cueLoops.Remove(id, out var loop)) CountEnd(loop.Cue);
            CancelLoopWatcher(slot);
            CancelWatcher(_loopPitchWatchers, slot);
            _stoppingLoops.Remove(slot);
            _loopFollows.Remove(slot);
            _loopSources.Remove(slot);
            if (source == null) return;

            source.Stop();
            Unspatialize(source);
            source.bypassReverbZones = true;
            source.priority = UnityDefaultPriority;
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
            PrepareSpatial(source);
            return source;
        }

        public SoundHandle Play(SoundCue? cue, float volume = 1f, int variant = -1, float fadeIn = 0f)
        {
            var source = RentCue(cue, variant, follow: null, out var resource);
            if (source == null) return SoundHandle.Invalid;

            source.spatialBlend = 0f;
            var spec = SpecOf(cue!, resource);
            var handle = Start(source, in spec, Mathf.Min(1f, LevelOf(cue!) * volume), fadeIn, follow: null, spatial: false);
            if (_logPlays) Log.Info($"sound {cue!.name} flat {Played(cue!)}");
            return handle;
        }

        public SoundHandle PlayAt(SoundCue? cue, Vector3 point, float volume = 1f, int variant = -1, float fadeIn = 0f)
        {
            var source = RentCue(cue, variant, follow: null, out var resource);
            if (source == null) return SoundHandle.Invalid;

            source.transform.position = point;
            source.spatialBlend = 1f;
            var spec = SpecOf(cue!, resource);
            var handle = Start(source, in spec, Mathf.Min(1f, LevelOf(cue!) * volume), fadeIn, follow: null, spatial: true);
            if (_logPlays) Log.Info($"sound {cue!.name} at {point} {Played(cue!)}");
            return handle;
        }

        public SoundHandle PlayAttached(SoundCue? cue, Transform follow, float volume = 1f, int variant = -1, float fadeIn = 0f)
        {
            if (follow == null) return SoundHandle.Invalid;
            var source = RentCue(cue, variant, follow, out var resource);
            if (source == null) return SoundHandle.Invalid;

            source.transform.position = follow.position;
            source.spatialBlend = 1f;
            var spec = SpecOf(cue!, resource);
            var handle = Start(source, in spec, Mathf.Min(1f, LevelOf(cue!) * volume), fadeIn, follow, spatial: true);
            if (_logPlays) Log.Info($"sound {cue!.name} {follow.name} {Played(cue!)}");
            return handle;
        }

        public LoopHandle Loop(SoundCue? cue, float volume = 1f, float fadeIn = 0f) =>
            StartCueLoop(cue, volume, fadeIn, point: null, follow: null);

        public LoopHandle LoopAt(SoundCue? cue, Vector3 point, float volume = 1f, float fadeIn = 0f) =>
            StartCueLoop(cue, volume, fadeIn, point, follow: null);

        public LoopHandle LoopAttached(SoundCue? cue, Transform follow, float volume = 1f, float fadeIn = 0f) =>
            follow == null ? LoopHandle.Invalid : StartCueLoop(cue, volume, fadeIn, point: null, follow);

        // A loop is a slot under a key of its own, built once here; the slot machinery does the rest.
        private LoopHandle StartCueLoop(SoundCue? cue, float volume, float fadeIn, Vector3? point, Transform? follow)
        {
            if (cue == null || cue.Variants == null || cue.Variants.Length == 0) return LoopHandle.Invalid;

            _cues.TryGetValue(cue, out var state);
            if (!PassesCueLimits(cue, in state))
            {
                state.Dropped++;
                _cues[cue] = state;
                return LoopHandle.Invalid;
            }

            var resource = cue.Variants[CuePick.Next(cue, ref state, -1, _random)];
            if (resource == null)
            {
                _cues[cue] = state;
                return LoopHandle.Invalid;
            }
            CountStart(ref state);
            _cues[cue] = state;

            long id = _nextHandleId++;
            string key = $"{cue.name}#{id}";
            float level = LevelOf(cue);
            var spec = SpecOf(cue, resource);
            bool spatial = point.HasValue || follow != null;
            var source = StartLoop(key, in spec, Mathf.Min(1f, level * volume), fadeIn, spatial ? 1f : 0f);
            _cueLoops[id] = new CueLoop(key, cue, level);
            _loopIds[key] = id;

            if (point.HasValue) source.transform.position = point.Value;
            if (follow != null)
            {
                source.transform.position = follow.position;
                _loopFollows[key] = follow;
                if (!_followingLoops) FollowLoopsAsync(_cts.Token).Forget();
            }

            if (_logPlays)
            {
                string where = follow != null ? follow.name : point.HasValue ? $"at {point.Value}" : "flat";
                Log.Info($"sound {cue.name} {where} {state.Played}");
            }
            return new LoopHandle(id);
        }

        public void SetVolume(LoopHandle loop, float volume, float duration = 0f)
        {
            if (!_cueLoops.TryGetValue(loop.Id, out var live)) return;
            SetLoopVolume(live.Key, Mathf.Min(1f, live.Level * volume), duration);
        }

        public void SetPitch(LoopHandle loop, float pitch, float duration = 0f)
        {
            if (_cueLoops.TryGetValue(loop.Id, out var live)) SetLoopPitch(live.Key, pitch, duration);
        }

        public void Stop(LoopHandle loop, float fadeOut = 0f)
        {
            if (!_cueLoops.TryGetValue(loop.Id, out var live)) return;
            // A loop already fading out keeps its fade; a cut ends it now.
            if (fadeOut > 0f && _stoppingLoops.Contains(live.Key)) return;
            StopLoop(live.Key, fadeOut);
        }

        public bool IsPlaying(LoopHandle loop) =>
            _cueLoops.TryGetValue(loop.Id, out var live) && !_stoppingLoops.Contains(live.Key) && IsLoopPlaying(live.Key);

        public SpatialHandle Adopt(AudioSource source, in SourceSetup setup)
        {
            if (source == null) return SpatialHandle.Invalid;
            Release(source);

            var kind = setup.Kind;
            source.outputAudioMixerGroup = setup.Output != null ? setup.Output
                : kind != null && kind.Output != null ? kind.Output : _sfxGroup;
            source.ignoreListenerPause = setup.PlaysOnPause;

            var handle = SpatialHandle.Invalid;
            if (kind == null) source.bypassReverbZones = true;
            else if (_space != null) handle = _space.Add(source, kind, setup.Volume, setup.Spatial);
            else SoundSpace.Shape(source, kind);

            source.priority = SoundPriority.ToUnity(setup.Priority);
            _adopted[source] = new Adopted(handle, kind != null);
            return handle;
        }

        public void Release(AudioSource source)
        {
            // A source destroyed with its holder is still forgotten: Unity's null would keep it here for good.
            if (ReferenceEquals(source, null) || !_adopted.Remove(source, out var adopted)) return;
            if (adopted.Handle.IsValid) _space?.Remove(adopted.Handle);
            if (adopted.Shaped && source != null) SoundSpace.Unshape(source);
        }

        public bool IsAdopted(AudioSource source) => !ReferenceEquals(source, null) && _adopted.ContainsKey(source);

        public AudioMixerGroup? GroupOf(SoundCue cue) => cue != null ? cue.OutputOr(_sfxGroup) : null;

        public int Played(SoundCue cue) => cue != null && _cues.TryGetValue(cue, out var state) ? state.Played : 0;

        public void Describe(List<CueStats> into)
        {
            foreach (var (cue, state) in _cues)
                if (cue != null) into.Add(new CueStats(cue, state.Played, state.Dropped, state.Playing, state.Last));
        }

        public bool LogPlays
        {
            get => _logPlays;
            set => _logPlays = value;
        }

        /// <summary>The random numbers variant picks use (tests, seeded runs). Null: UnityEngine.Random.</summary>
        public ICueRandom CueRandom
        {
            get => _random;
            set => _random = value ?? UnityCueRandom.Shared;
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
                float startVolume = source != null ? VolumeOf(source) : 0f;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += DeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    if (source != null)
                    {
                        SetVolumeOf(source, startVolume * (1f - t));
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
            if (_limiterAdded && _limiter != null) Object.Destroy(_limiter);
            _limiter = null;
            if (_root != null)
            {
                if (Application.isPlaying) Object.Destroy(_root);
                else Object.DestroyImmediate(_root);
            }
        }

        // Priority: the rank on the cue scale (a SoundConfig's is Default + its Priority).
        private readonly struct Voice
        {
            public readonly int Priority;
            public readonly AudioResource Resource;
            public readonly SoundCue? Cue;
            public readonly Transform? Follow;

            public Voice(int priority, AudioResource resource, SoundCue? cue, Transform? follow)
            {
                Priority = priority;
                Resource = resource;
                Cue = cue;
                Follow = follow;
            }
        }

        // What one start sets on its source. UnityPriority -1: the SoundConfig path's own rule (ApplyPriority).
        private readonly struct Spec
        {
            public readonly AudioResource Resource;
            public readonly AudioMixerGroup? Output;
            public readonly AudioMixerGroup? SpatialOutput;
            public readonly SoundKind? Kind;
            public readonly float PitchJitter;
            public readonly bool PlaysOnPause;
            public readonly int UnityPriority;

            public Spec(AudioResource resource, AudioMixerGroup? output, AudioMixerGroup? spatialOutput, SoundKind? kind,
                float pitchJitter, bool playsOnPause, int unityPriority)
            {
                Resource = resource;
                Output = output;
                SpatialOutput = spatialOutput;
                Kind = kind;
                PitchJitter = pitchJitter;
                PlaysOnPause = playsOnPause;
                UnityPriority = unityPriority;
            }
        }

        private readonly struct CueLoop
        {
            public readonly string Key;
            public readonly SoundCue Cue;
            // cue.Volume with the start's jitter: what SetVolume scales.
            public readonly float Level;

            public CueLoop(string key, SoundCue cue, float level)
            {
                Key = key;
                Cue = cue;
                Level = level;
            }
        }

        private readonly struct Adopted
        {
            public readonly SpatialHandle Handle;
            public readonly bool Shaped;

            public Adopted(SpatialHandle handle, bool shaped)
            {
                Handle = handle;
                Shaped = shaped;
            }
        }
    }
}
