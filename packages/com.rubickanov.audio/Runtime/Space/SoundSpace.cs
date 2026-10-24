using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Rubickanov.Audio
{
    /// <summary>A source registered with a <see cref="SoundSpace"/>.</summary>
    public readonly struct SpatialHandle : IEquatable<SpatialHandle>
    {
        public static readonly SpatialHandle Invalid = default;

        internal readonly int Slot;
        internal readonly int Gen;

        internal SpatialHandle(int slot, int gen)
        {
            Slot = slot;
            Gen = gen;
        }

        public bool IsValid => Gen != 0;

        public bool Equals(SpatialHandle other) => Slot == other.Slot && Gen == other.Gen;
        public override bool Equals(object? obj) => obj is SpatialHandle other && Equals(other);
        public override int GetHashCode() => (Slot * 397) ^ Gen;
        public static bool operator ==(SpatialHandle a, SpatialHandle b) => a.Equals(b);
        public static bool operator !=(SpatialHandle a, SpatialHandle b) => !a.Equals(b);
    }

    [Flags]
    public enum SpatialOptions
    {
        None = 0,
        /// <summary>The space leaves <see cref="AudioSource.volume"/> alone; the owner applies <see cref="SoundSpace.GainOf"/>.</summary>
        ExternalGain = 1,
    }

    /// <summary>A registered source as <see cref="SoundSpace.Describe"/> sees it.</summary>
    public readonly struct SpatialState
    {
        public readonly AudioSource Source;
        public readonly SoundKind Kind;
        /// <summary>What the owner wants, before shadow and occlusion.</summary>
        public readonly float Volume;
        /// <summary>Head shadow × occlusion.</summary>
        public readonly float Gain;
        public readonly float Back;
        public readonly float Occlusion;
        public readonly float Cutoff;

        public SpatialState(AudioSource source, SoundKind kind, float volume, float gain, float back, float occlusion, float cutoff)
        {
            Source = source;
            Kind = kind;
            Volume = volume;
            Gain = gain;
            Back = back;
            Occlusion = occlusion;
            Cutoff = cutoff;
        }
    }

    /// <summary>Numbers shared by every kind: occlusion, head shadow and the outdoor reverb zone.</summary>
    [Serializable]
    public sealed class SoundSpaceSettings
    {
        [Tooltip("Occlusion rays per frame, round-robin over the occluded sounds (a sound's full look takes 8).")]
        public int RaysPerFrame = 16;
        [Tooltip("Seconds (time constant) into the shadow of an obstacle.")]
        public float OcclusionAttack = 0.12f;
        [Tooltip("Seconds (time constant) out of it.")]
        public float OcclusionRelease = 0.3f;
        [Tooltip("Level drop at full occlusion, dB.")]
        public float OcclusionDb = 9f;
        [Tooltip("Cutoff multiplier at full occlusion.")]
        public float OcclusionCutoff = 0.3f;
        [Tooltip("Level drop straight behind the listener, dB.")]
        public float ShadowDb = 1.25f;
        [Tooltip("Cutoff multiplier straight behind the listener (over the air's cutoff, as occlusion's).")]
        public float ShadowCutoff = 0.55f;
        [Tooltip("Seconds (time constant) the head shadow follows a turn with, so a fast turn sweeps the filter.")]
        public float ShadowSmoothing = 0.06f;
        [Tooltip("One reverb zone on the listener for the kinds' outdoor reverb (the game places it).")]
        public bool OutdoorReverb = true;
        public int ReverbRoom = -1200;
        public int ReverbRoomHF = -900;
        public float ReverbDecay = 1.1f;
        public int ReflectionsDelayMs = 60;
    }

    /// <summary>
    /// Applies <see cref="SoundKind"/>s to sources: their static part once (<see cref="Shape"/>), head shadow and
    /// occlusion per frame (<see cref="Tick"/>) over one list, without a component per source.
    /// </summary>
    public sealed class SoundSpace : IDisposable
    {
        // A source resting on the ground must not be occluded by the ground under it.
        private const float RayShortfall = 0.3f;
        private const float FindEvery = 1f;

        // Sources a Shape touched, for Strays.
        private static readonly HashSet<AudioSource> Shaped = new();
        // Filters carrying the static air curve: a ticking kind must clear it before writing the cutoff.
        private static readonly HashSet<AudioLowPassFilter> Curved = new();
        private static readonly AnimationCurve NoCurve = new();

        private readonly SoundSpaceSettings _settings;
        private readonly Stack<int> _free = new();
        private Entry[] _entries = new Entry[32];
        private int _top;
        private int _count;
        private int _occluders;
        private int _rayCursor;
        private int _gen;
        private Transform? _listener;
        private Transform? _found;
        private AudioListener? _foundEars;
        private float _nextFind;

        public SoundSpace(SoundSpaceSettings? settings = null) => _settings = settings ?? new SoundSpaceSettings();

        public SoundSpaceSettings Settings => _settings;

        /// <summary>The ears. Null: the enabled AudioListener, found once and cached.</summary>
        public Transform? Listener
        {
            get => _listener != null ? _listener : _found;
            set => _listener = value;
        }

        /// <summary>False when the game calls <see cref="Tick"/> itself; true lets the audio service tick it.</summary>
        public bool TicksItself { get; set; } = true;

        public int Count => _count;

        /// <summary>False: nothing is occluded (A/B checks); the smoothing still eases the change.</summary>
        public bool OcclusionOn { get; set; } = true;

        // Test hook: (from, to, mask) → how much blocks the way, 0..1.
        internal Func<Vector3, Vector3, int, float> Blocked = RayBlock;
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        /// <summary>
        /// The kind's static part: rolloff, spread and reverb-mix curves, max distance, priority, the low-pass (air
        /// curve, or left to <see cref="Tick"/> for a kind that ticks). No per-frame cost.
        /// </summary>
        public static void Shape(AudioSource source, SoundKind kind)
        {
            KindCurves curves = KindCurves.Of(kind);
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            // A kind without reverb stays out of the zones: their send is taken before the source's filters, so a
            // source that makes its sound in OnAudioFilterRead (voice chat) would send what its clip holds.
            source.bypassReverbZones = kind.ReverbAtReference <= 0f || kind.ReverbMax <= 0f;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.maxDistance = kind.Max;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curves.Rolloff);
            source.SetCustomCurve(AudioSourceCurveType.Spread, curves.Spread);
            source.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, curves.Reverb);
            source.priority = kind.Priority;
            if (kind.Output != null && source.outputAudioMixerGroup == null) source.outputAudioMixerGroup = kind.Output;

            var filter = source.GetComponent<AudioLowPassFilter>();
            if (filter == null) filter = source.gameObject.AddComponent<AudioLowPassFilter>();
            // Unity's default Q 1 bumps +1.25 dB at the cutoff: a moving cutoff would sound like a wah.
            filter.lowpassResonanceQ = 0.707f;
            if (kind.Ticks)
            {
                if (Curved.Remove(filter)) filter.customCutoffCurve = NoCurve;
                filter.cutoffFrequency = Mathf.Min(SoundMath.AirCutoff(0f, kind.Air), SoundMath.MaxCutoff);
                filter.enabled = true;
            }
            else
            {
                filter.customCutoffCurve = curves.Cutoff;
                Curved.Add(filter);
                filter.enabled = kind.Air > 0f;
            }

            Shaped.Add(source);
        }

        /// <summary>
        /// Undoes <see cref="Shape"/> to Unity's defaults for a pooled source going back: no custom curves, filter off,
        /// reverb zones bypassed. The owner re-applies its own rolloff.
        /// </summary>
        public static void Unshape(AudioSource source)
        {
            Shaped.Remove(source);
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.spread = 0f;
            source.reverbZoneMix = 1f;
            source.bypassReverbZones = true;
            source.priority = 128;
            var filter = source.GetComponent<AudioLowPassFilter>();
            if (filter == null) return;
            if (Curved.Remove(filter)) filter.customCutoffCurve = NoCurve;
            filter.cutoffFrequency = SoundMath.MaxCutoff;
            filter.enabled = false;
        }

        /// <summary>
        /// <see cref="Shape"/> plus, for a kind that ticks, head shadow and occlusion every frame. volume: what the
        /// owner wants before them; later changes go through <see cref="SetVolume"/>.
        /// </summary>
        public SpatialHandle Add(AudioSource source, SoundKind kind, float volume = 1f, SpatialOptions options = default)
        {
            if (source == null || kind == null) return SpatialHandle.Invalid;
            Shape(source, kind);

            int slot;
            if (_free.Count > 0)
            {
                slot = _free.Pop();
            }
            else
            {
                if (_top == _entries.Length) Array.Resize(ref _entries, _entries.Length * 2);
                slot = _top++;
            }

            if (++_gen == 0) _gen = 1;
            ref Entry e = ref _entries[slot];
            e = new Entry
            {
                Source = source,
                Filter = source.GetComponent<AudioLowPassFilter>(),
                Kind = kind,
                Volume = volume,
                Gain = 1f,
                Gen = _gen,
                Alive = true,
                Ticks = kind.Ticks,
                Occludes = kind.Occlusion,
                External = (options & SpatialOptions.ExternalGain) != 0,
            };
            _count++;
            if (e.Occludes) _occluders++;

            // The first buffer already sounds where the source is: no swell from open to occluded.
            Transform? ears = ResolveListener();
            if (e.Ticks && ears != null)
            {
                if (e.Occludes)
                    for (int i = 0; i < SoundMath.OcclusionPoints; i++) Look(ref e, ears.position);
                Step(ref e, ears.position, ears.forward, 0f, snap: true);
            }
            else
            {
                source.volume = volume;
            }

            return new SpatialHandle(slot, _gen);
        }

        /// <summary>The owner's volume (fades write here, not to the source, which carries the shadow and occlusion).</summary>
        public void SetVolume(SpatialHandle handle, float volume)
        {
            if (!TryGet(handle, out int slot)) return;
            ref Entry e = ref _entries[slot];
            e.Volume = volume;
            if (e.Source != null) e.Source.volume = e.External ? volume : volume * e.Gain;
        }

        /// <summary>The owner's volume last set; 0 for a handle no longer registered.</summary>
        public float VolumeOf(SpatialHandle handle) => TryGet(handle, out int slot) ? _entries[slot].Volume : 0f;

        /// <summary>Head shadow × occlusion as a gain, for <see cref="SpatialOptions.ExternalGain"/> owners.</summary>
        public float GainOf(SpatialHandle handle) => TryGet(handle, out int slot) ? _entries[slot].Gain : 1f;

        /// <summary>0 open … 1 fully occluded, smoothed.</summary>
        public float OcclusionOf(SpatialHandle handle) => TryGet(handle, out int slot) ? _entries[slot].Occ : 0f;

        public bool Contains(SpatialHandle handle) => TryGet(handle, out _);

        /// <summary>What each registered source is now (consoles, checks): appended to <paramref name="into"/>.</summary>
        public void Describe(List<SpatialState> into)
        {
            for (int i = 0; i < _top; i++)
            {
                ref Entry e = ref _entries[i];
                if (!e.Alive || e.Source == null) continue;
                into.Add(new SpatialState(e.Source, e.Kind, e.Volume, e.Gain, e.Back, e.Occ,
                    e.Filter != null && e.Filter.enabled ? e.Filter.cutoffFrequency : SoundMath.MaxCutoff));
            }
        }

        /// <summary>The settings' outdoor reverb on a zone (User preset); its radius is left to the caller.</summary>
        public static void ApplyReverb(AudioReverbZone zone, SoundSpaceSettings settings)
        {
            zone.reverbPreset = AudioReverbPreset.User;
            zone.room = settings.ReverbRoom;
            zone.roomHF = settings.ReverbRoomHF;
            zone.decayTime = settings.ReverbDecay;
            zone.reflectionsDelay = settings.ReflectionsDelayMs / 1000f;
        }

        /// <summary>Stops ticking the source. Its shape stays; <see cref="Unshape"/> undoes it.</summary>
        public void Remove(SpatialHandle handle)
        {
            if (!TryGet(handle, out int slot)) return;
            Free(slot);
        }

        public void Tick(float deltaTime)
        {
            Transform? ears = ResolveListener();
            if (ears == null || _count == 0) return;
            Vector3 at = ears.position, forward = ears.forward;

            CastRays(at);
            for (int i = 0; i < _top; i++)
            {
                ref Entry e = ref _entries[i];
                if (!e.Alive || !e.Ticks) continue;
                if (e.Source == null || e.Filter == null)
                {
                    Free(i);
                    continue;
                }
                Step(ref e, at, forward, deltaTime, snap: false);
            }
        }

        /// <summary>
        /// Scene sources that would send to a reverb zone (not bypassed, mix above 0) without a kind's shape: what
        /// still has to opt out at its creation site. A check, not a fix.
        /// </summary>
        public static List<AudioSource> Strays()
        {
            Shaped.RemoveWhere(s => s == null);
            Curved.RemoveWhere(f => f == null);
            var strays = new List<AudioSource>();
            foreach (AudioSource s in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude))
                if (!s.bypassReverbZones && s.reverbZoneMix > 0f && !Shaped.Contains(s)) strays.Add(s);
            return strays;
        }

        public void Dispose()
        {
            Array.Clear(_entries, 0, _entries.Length);
            _free.Clear();
            _top = _count = _occluders = 0;
        }

        // One ray each, round-robin: every occluded entry gets a ray within ⌈occluded / RaysPerFrame⌉ frames, and a
        // few entries get several a frame, up to their whole look.
        private void CastRays(Vector3 at)
        {
            if (_occluders == 0) return;
            int budget = Mathf.Max(1, _settings.RaysPerFrame);
            for (int scanned = 0; scanned < _top * SoundMath.OcclusionPoints && budget > 0; scanned++)
            {
                if (_rayCursor >= _top) _rayCursor = 0;
                ref Entry e = ref _entries[_rayCursor++];
                if (!e.Alive || !e.Occludes || e.Source == null) continue;
                Look(ref e, at);
                budget--;
            }
        }

        // The entry's next point: how much blocks the way there, kept per point; the target is their mean.
        private void Look(ref Entry e, Vector3 at)
        {
            int point = e.Point;
            e.Point = (byte)((point + 1) % SoundMath.OcclusionPoints);
            float blocked = 0f;
            if (OcclusionOn)
            {
                Vector3 to = SoundMath.OcclusionPoint(at, e.Source.transform.position, e.Kind.OcclusionRadius, point);
                blocked = Mathf.Clamp01(Blocked(at, to, e.Kind.OcclusionMask));
            }

            ulong level = (ulong)Mathf.RoundToInt(blocked * 255f);
            if (e.Kind.OcclusionRadius <= 0f)
            {
                // One point only: it stands for all of them.
                e.Points = level * 0x0101010101010101UL;
                e.OccTarget = level / 255f;
                return;
            }

            int shift = point * 8;
            e.Points = (e.Points & ~(0xFFUL << shift)) | (level << shift);
            int sum = 0;
            for (ulong p = e.Points; p != 0; p >>= 8) sum += (int)(p & 0xFF);
            e.OccTarget = sum / (255f * SoundMath.OcclusionPoints);
        }

        private void Step(ref Entry e, Vector3 at, Vector3 forward, float dt, bool snap)
        {
            SoundSpaceSettings s = _settings;
            Vector3 to = e.Source.transform.position - at;
            float back = SoundMath.BackAmount(forward, to) * e.Kind.HeadShadow;
            e.Back = snap ? back : SoundMath.Smooth(e.Back, back, dt, s.ShadowSmoothing);
            e.Occ = snap ? e.OccTarget : SoundMath.Smooth(e.Occ, e.OccTarget, dt, s.OcclusionAttack, s.OcclusionRelease);

            float cutoff = SoundMath.AirCutoff(to.magnitude, e.Kind.Air) * Mathf.Lerp(1f, s.ShadowCutoff, e.Back);
            cutoff *= Mathf.Lerp(1f, s.OcclusionCutoff, e.Occ);
            e.Filter.cutoffFrequency = Mathf.Clamp(cutoff, 10f, SoundMath.MaxCutoff);
            e.Gain = SoundMath.DbToGain(-s.ShadowDb * e.Back - s.OcclusionDb * e.Occ);
            e.Source.volume = e.External ? e.Volume : e.Volume * e.Gain;
        }

        private Transform? ResolveListener()
        {
            if (_listener != null) return _listener;
            // A listener switched off (a probe's own ears, a camera swap) is replaced at once.
            if (_found != null && _foundEars != null && _foundEars.isActiveAndEnabled) return _found;
            float now = Time.realtimeSinceStartup;
            if (now < _nextFind && _found == null) return null;
            _nextFind = now + FindEvery;
            _found = null;
            foreach (AudioListener l in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
            {
                if (!l.isActiveAndEnabled) continue;
                _found = l.transform;
                _foundEars = l;
                break;
            }
            return _found;
        }

        private bool TryGet(SpatialHandle handle, out int slot)
        {
            slot = handle.Slot;
            return handle.IsValid && slot >= 0 && slot < _top && _entries[slot].Alive && _entries[slot].Gen == handle.Gen;
        }

        private void Free(int slot)
        {
            ref Entry e = ref _entries[slot];
            if (e.Occludes) _occluders--;
            e = default;
            _free.Push(slot);
            _count--;
        }

        // The most any collider on the way blocks (SoundOccluder.Of), 0 when none.
        internal static float RayBlock(Vector3 from, Vector3 to, int mask)
        {
            Vector3 d = to - from;
            float distance = d.magnitude;
            if (distance <= RayShortfall) return 0f;
            int n = Physics.RaycastNonAlloc(from, d / distance, Hits, distance - RayShortfall, mask, QueryTriggerInteraction.Ignore);
            float most = 0f;
            for (int i = 0; i < n && most < 1f; i++) most = Mathf.Max(most, SoundOccluder.Of(Hits[i].collider));
            return most;
        }

        private struct Entry
        {
            public AudioSource Source;
            public AudioLowPassFilter Filter;
            public SoundKind Kind;
            public float Volume;
            public float Back;
            public float Occ;
            public float OccTarget;
            // Per occlusion point, 0..255 blocked; the next point to look at.
            public ulong Points;
            public byte Point;
            public float Gain;
            public int Gen;
            public bool Alive;
            public bool Ticks;
            public bool Occludes;
            public bool External;
        }
    }
}
