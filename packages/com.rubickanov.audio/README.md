# Audio

Audio service with SFX source pooling (priority eviction, per-sound repeat limits), loop slots in 2D, at a point or attached to a transform with live volume and pitch, music crossfade, fade in/out, sounds that play through a paused listener, mixer snapshots, and mixer volume control; 3D sound kinds (inverse-distance level, air absorption, width by distance, outdoor reverb send, head shadow, occlusion) on Unity's own `AudioSource`, a lookahead true-peak limiter on the listener's output, a mixer layout kit for the editor, and an output capture for checks.

## Dependencies

> `UniTask` comes from a git URL, not from UPM — UPM will not pull it in for you. See [Third-party dependencies](https://github.com/rubickanov/unity-packages#third-party-dependencies).

- `UniTask` — async crossfade, fades, ramps, and source lifecycle watchers

Requires Unity 6000.0 or newer (uses `AudioResource` and `AudioSource.resource`).

## Architecture

```
IAudioService
├── UnityAudioService    — AudioMixer-based, pooled SFX sources, named loop slots, music crossfade
└── NullAudioService     — no-op for server/headless builds

SoundSpace               — applies SoundKinds to sources: static curves once, head shadow + occlusion per frame
├── SoundKind            — ScriptableObject: what a kind of 3D sound does with distance and the listener
└── SoundMath            — the numbers: distance law, air cutoff, spread, reverb send, back amount, curves

OutputLimiter            — lookahead true-peak limiter on the active listener, after the whole mixer
└── TruePeakLimiter      — the DSP: 4× oversampled peaks, min-hold + ramp over the lookahead, release

MixerKit (Editor)        — ensures groups, effects, sends, exposed volumes and a limiter in an existing mixer
ListenerCapture          — records the final output (or the listener) to a WAV; editor and debug builds
```

**UnityAudioService** is constructed from an **AudioServiceConfig** (ScriptableObject) that supplies the mixer, the music and default SFX groups, pool size, default crossfade duration, 3D distance settings, the fade-out of a loop whose target is destroyed, and whether fades run on real time. The service creates a `[AudioService]` GameObject (marked `DontDestroyOnLoad` in play mode) that hosts the music sources, the SFX pool, and loop sources.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.Audio.Runtime** | Yes | Service, configs, sound kinds and space, output limiter, capture |
| **Rubickanov.Audio.Editor** | Editor | `MixerKit`: mixer layout from code |

## Core Concepts

**SoundConfig** — Serializable struct with an `AudioResource`, a pitch variation range (`0`–`0.5`), an optional output `AudioMixerGroup`, and pool rules for one-shots: `Priority`, `MaxInstances`, `MinInterval`, and `PlaysOnPause`. Used for SFX and loops. Assigned in the Inspector. A sound with no output plays through the config's `SfxGroup`; set one to route crowd, UI or voice sounds to their own group.

**MusicConfig** — Serializable struct with an `AudioResource` and `PlaysOnPause`. No pitch variation. Used by `PlayMusic`.

**SoundHandle** — Opaque readonly struct returned by `PlaySFX*`. Pass it to `StopSound` to stop a one-shot early. Ignore it if you don't need to.

**SoundKind** — ScriptableObject (**Create > Config > Sound Kind**) describing a kind of 3D sound: the distance law (`Inverse`: full level within `Reference`, −6 dB per doubling past it, a smoothstep fade to silence over the last `FadeShare` of `Max`; `External`: flat, the owner sets the level), `Air` (high-frequency loss ×ISO 9613-1), `SpreadNear`/`SpreadFar`/`SpreadFarAt` (width by distance), `ReverbAtReference`/`ReverbMax` (reverb-zone send rising with distance), `HeadShadow`, `Occlusion` with its `OcclusionMask`, `Priority` and an optional `Output` group. A `SoundConfig` names its kind in `Kind`.

**SoundSpace** — Applies kinds to `AudioSource`s. `Shape` sets everything static once (custom rolloff, spread and reverb-mix curves, the air low-pass curve); `Add` also registers a source for the per-frame part (head shadow, occlusion) when the kind needs it (`kind.Ticks`). One list, no component per source. The service takes a space and uses it for every 3D play of a sound with a kind; a game adds its own sources (speakers, voices) itself.

**PropagationDelay** — Pure C# delay through the air for a source whose samples the game owns (a PCM reader): the read trails a reference cursor by distance / speed of sound, moving with the read rate within 1 ± `MaxSlew` and crossfading past `Jump`. Fractional reads by a 4-point Hermite.

**LoudnessMeter / LoudnessGain** — Pure C# BS.1770 loudness of a stream over a sliding window, and a slow gain bringing it to a target: what makes tracks from different sources play equally loud.

**Loop slot** — A string key identifying a dedicated, non-pooled `AudioSource`. `PlayLoop("steps", cfg)` reuses the same slot across calls and is never evicted by the SFX pool. A slot plays in 2D, at a point or following a transform.

## Quick Start

1. Create an `AudioServiceConfig` asset via **Create > Config > Audio Service**. Assign the mixer, the music group and the default SFX group; set the 3D distances to the range your camera hears from.
2. Construct the service:

```csharp
IAudioService audio = new UnityAudioService(audioConfig);
```

With 3D sound kinds, give it a space (its numbers come from `AudioServiceConfig.Space`):

```csharp
var space = new SoundSpace(audioConfig.Space);
IAudioService audio = new UnityAudioService(audioConfig, space);
```

Or register it in a DI container:

```csharp
builder.RegisterInstance(audioConfig);
builder.Register<IAudioService, UnityAudioService>(Lifetime.Singleton);
```

## Usage

### Playing SFX

```csharp
[SerializeField] private SoundConfig _hitSound;

audio.PlaySFX(_hitSound);
audio.PlaySFXAtPoint(_hitSound, enemy.transform.position);   // 3D, positioned
audio.PlaySFXAttached(_hitSound, enemy.transform);           // 3D, follows the transform

audio.PlaySFX(_hitSound, volumeScale: 0.5f);
audio.PlaySFX(_hitSound, fadeIn: 0.2f);
```

Positioned and attached sounds use the 3D settings from `AudioServiceConfig` (`Rolloff`, `MinDistance`, `MaxDistance`, `DopplerLevel`); the defaults match a fresh `AudioSource`.

A one-shot returns its source to the pool automatically when it finishes. An invalid `SoundConfig` (no resource) plays nothing and returns `SoundHandle.Invalid`.

### Stopping SFX

```csharp
SoundHandle handle = audio.PlaySFX(_alarmSound);
audio.StopSound(handle);                 // immediate
audio.StopSound(handle, fadeOut: 0.3f);  // fade out, then release to the pool; an attached sound keeps following

audio.StopAllSFX();                      // every pooled one-shot; loops and music keep playing
audio.StopAllSFX(fadeOut: 0.2f);
```

### Priority and Repeat Limits

Set in the Inspector on each `SoundConfig`; zeros mean no rule, so a new sound behaves like a plain pooled one-shot.

| Field | Effect |
|---|---|
| `Priority` | When the pool is full, the playing one-shot with the lowest priority is evicted, oldest first. A sound whose priority is below every playing one is dropped. One-shots fading out after `StopSound` go first, the quietest of them, whatever their priority. |
| `MaxInstances` | At most this many copies of the sound play at once; one more is dropped. |
| `MinInterval` | Seconds between two starts of the sound (`0.05` = at most once per 50 ms); a start inside the window is dropped. |

```csharp
// bumper: Priority 100; body hit: Priority 0, MaxInstances 4, MinInterval 0.05
audio.PlaySFX(_bumper);                              // never evicted by hits
SoundHandle hit = audio.PlaySFXAtPoint(_bodyHit, contact.point, volumeScale: force01);
if (!hit.IsValid) { /* dropped by a limit — nothing to do */ }
```

A dropped sound returns `SoundHandle.Invalid`. A sound is identified by its `AudioResource`, so two configs with the same resource share limits. Copies fading out after `StopSound` don't count. `MinInterval` runs on the same clock as the fades (real time by default). Loops and music are not pooled and ignore these fields.

### Loops via Named Slots

```csharp
[SerializeField] private SoundConfig _footsteps;
[SerializeField] private SoundConfig _wind;

audio.PlayLoop("steps", _footsteps);
audio.PlayLoop("ambient", _wind, fadeIn: 1f);

if (audio.IsLoopPlaying("steps"))
    audio.StopLoop("steps", fadeOut: 0.2f);
```

Calling `PlayLoop` on a live slot replaces the current sound on the same source. A slot ends when its loop stops (after its fade, if any); its source is kept and reused by the next slot that starts, so many short-lived slot names (`"piston-17"`) don't pile up sources.

Change a live loop without restarting it:

```csharp
audio.SetLoopVolume("crowd", 0.9f, duration: 0.5f);   // ramp to 0.9
audio.SetLoopPitch("wind", 1.3f, duration: 0.2f);     // absolute pitch, not a multiplier
```

Both are no-ops on a slot that is stopped or fading out, so a late update can't bring back a loop you stopped.

### Loops in 3D

```csharp
[SerializeField] private SoundConfig _spinnerHum;
[SerializeField] private SoundConfig _pistonMotor;

audio.PlayLoopAtPoint("spinner-1", _spinnerHum, spinner.position);          // fixed point
audio.PlayLoopAttached("piston-1", _pistonMotor, piston.transform);        // follows the transform

audio.SetLoopPitch("piston-1", 1.2f, duration: 0.2f);   // live changes work the same as in 2D
audio.StopLoop("piston-1", fadeOut: 0.5f);              // keeps following while it fades
```

`PlayLoop`, `PlayLoopAtPoint` and `PlayLoopAttached` all drive the same slot; the last call decides where it plays, so `PlayLoop` on an attached slot makes it 2D and stops following. Positioned loops use the same 3D settings from `AudioServiceConfig` as positioned one-shots. `PlayLoopAttached` with a `null` transform does nothing and leaves the slot as it was.

When the followed transform is destroyed, the loop fades out where it was last seen over `AudioServiceConfig.LostTargetFadeOut` (default `0.25` s; `0` stops at once). A loop already fading out after `StopLoop` keeps its own fade. A disabled but living transform is still followed and keeps playing: stop the loop in `OnDisable` if the sound belongs to the object's active state.

Slots outlive scenes. An attached loop goes quiet on a scene load by itself, because its transform is destroyed; a loop at a point keeps playing until you stop it.

### Music

```csharp
[SerializeField] private MusicConfig _battleTheme;

audio.PlayMusic(_battleTheme);                          // default crossfade
audio.PlayMusic(_battleTheme, crossfadeDuration: 3f);   // per-call override
audio.StopMusic();
```

`PlayMusic` with the track already playing keeps it going instead of starting it over, so a level can ask for its music on every restart; a track that is fading in or out picks up from its current volume. To start a track from the beginning, call `StopMusic` first.

Music uses three sources. Switching tracks mid-crossfade cancels the in-flight transition and fades every audible track out from its current volume, so volumes never snap; only a third switch inside one crossfade cuts the quietest track.

### Pause

`AudioListener.pause = true` pauses every sound; unpausing resumes them where they were. While paused, Unity reports `AudioSource.isPlaying` as `false`, so the service treats a paused source as still busy: a one-shot keeps its source and handle until it really ends after the pause, an attached one-shot keeps following, and `IsLoopPlaying` stays `true` for a paused loop.

Menu clicks, pause-menu music and on-air graphics that must keep playing on the pause screen set `PlaysOnPause` on their `SoundConfig` or `MusicConfig` (it sets `AudioSource.ignoreListenerPause`):

```csharp
[SerializeField] private SoundConfig _menuClick;    // PlaysOnPause ticked in the Inspector
[SerializeField] private MusicConfig _pauseTheme;   // PlaysOnPause ticked

AudioListener.pause = true;
audio.PlaySFX(_menuClick);       // heard
audio.PlayMusic(_pauseTheme);    // heard; the game track it replaces fades out silently
audio.PlaySFX(_bodyHit);         // starts paused, plays after unpause
```

A container (`AudioRandomContainer`) that plays on pause is heard, but Unity keeps its source's `isPlaying` true until the pause ends, so the service ends such a one-shot once its output has stayed silent for 0.2 s; otherwise every menu click would hold a source, and its `MaxInstances`, for the whole pause.

The service doesn't pause anything itself: setting `AudioListener.pause` is the game's call. Fades keep running while paused, so a sound faded out on the pause screen is gone when the game resumes.

### 3D Sound Kinds

Create a `SoundKind` asset per kind of sound (a speaker, a footstep, a shutter) and assign it in the `Kind` field of the `SoundConfig`. Point and attached plays of that sound go through the service's space; 2D plays and sounds without a kind take the old path (the config's global rolloff) unchanged.

```csharp
audio.PlaySFXAtPoint(sounds.Shutter, phone.position);           // Shutter.Kind = "Click" kind
audio.PlayLoopAttached("radio", sounds.Radio, radio.transform);
```

The output group of a kind's sound is the sound's `Output`, else the kind's `Output`, else `SfxGroup`. Fades, `SetLoopVolume` and the pool's "quietest fading" choice work on the volume the caller asked for; the source's own `volume` also carries head shadow and occlusion.

What each kind sets:

| Field | Effect |
|---|---|
| `Reference`, `Max`, `FadeShare` | Custom rolloff: 1 up to `Reference`, `Reference / d` past it, smoothstep to 0 over the last `FadeShare` of `Max` (0: the law holds up to `Max`) |
| `Air` | Low-pass where air absorption takes 3 dB: 3 m → 13.9 kHz, 10 m → 7.6 kHz, 40 m → 3.8 kHz at `Air` 4 |
| `SpreadNear` → `SpreadFar` at `SpreadFarAt` | Width blended on log distance from `Reference`; ≤ 110° keeps a side source ≥ 6 dB louder in its ear (Unity's spread 0 is a hard pan, 180° has no direction) |
| `ReverbAtReference`, `ReverbMax` | Reverb-zone send `min(ReverbMax, ReverbAtReference · d / Reference)` |
| `HeadShadow` | Straight behind: −1.25 dB and the cutoff × 0.55, near an octave duller (`SoundSpaceSettings.ShadowDb`, `ShadowCutoff`), smoothed over 60 ms |
| `Occlusion`, `OcclusionMask` | A ray from the listener to the source (round-robin, `RaysPerFrame`); fully occluded: −9 dB and ×0.3 cutoff, attack 0.12 s, release 0.3 s |

Unity's panner loses up to 3 dB as a mono source widens (measured: −1.28 dB at 90°, −3.04 dB at 180°); the rolloff curve undoes it relative to the kind's widest spread, so width never changes loudness.

### Own Sources in the Space

```csharp
SpatialHandle h = space.Add(source, kinds.Speaker, volume: 0.8f);
space.SetVolume(h, 0.5f);             // owners with fades write here, never to source.volume
space.Remove(h);                      // stops ticking; SoundSpace.Unshape(source) undoes the shape
SoundSpace.Shape(source, kinds.Bird); // static part only, no per-frame cost
```

`SpatialOptions.ExternalGain` leaves `volume` to the owner, who multiplies by `space.GainOf(h)` itself (a voice chat with its own level path).

`space.Describe(list)` appends a `SpatialState` per registered source (kind, owned volume, shadow × occlusion gain, cutoff) for consoles and checks; `space.OcclusionOn = false` opens every occlusion for an A/B (the smoothing still eases it). `SoundSpace.ApplyReverb(zone, settings)` puts the settings' outdoor reverb (User preset: room, roomHF, decay, reflections delay) on a zone the game places; the radius is the game's.

The listener is `space.Listener`, or the enabled `AudioListener` found once and cached (a cached listener that is switched off is replaced at once). The service ticks the space after `LateUpdate`; a game with its own frame order sets `space.TicksItself = false` and calls `space.Tick(deltaTime)` after its camera moves.

### Reverb Zones

An `AudioReverbZone` takes a send from **every** source inside it with `reverbZoneMix > 0` — Unity's default — 2D or 3D. So:

- every source the service creates (pool, loop slots, music) bypasses reverb zones; `Shape` turns the bypass off for kinds with a reverb send and keeps it on for a kind whose `ReverbAtReference` or `ReverbMax` is 0 (a voice chat's playback makes its sound in `OnAudioFilterRead` over a flat clip: its send would be the clip, a thump, not the voice);
- a game's own sources that should stay dry set `bypassReverbZones = true` where they are made;
- `SoundSpace.Strays()` lists scene sources that would send without a kind's shape — a check, not a fix.

Measured on 6000.6 (keep these in mind when sizing the send):

- the send is taken **after** volume, rolloff and every mixer fader on the source's path, but **before** the source's own filters and `OnAudioFilterRead` — a low-passed or script-generated source sends its raw signal;
- the wet sums **after** the mixer **and after the listener's filters**: neither a Master limiter nor the [Output Limiter](#output-limiter) sees it. Size every send so its wet fits the limiter's headroom (ceiling to 0 dBTP). An `Inverse` kind: keep `ReverbAtReference` ≤ 0.1 — with send ∝ distance and level ∝ 1/distance its wet stays near its level at `Reference` minus 26 dB minus the zone's own level. An `External` kind has a fixed send while the game sets the level: the louder it plays, the louder its wet, also while the limiter is cutting its dry sound. Measured with a −12 dB zone (decay 1.1 s) and thunder that drives the limiter 5 dB deep: send 1.0 → clipped at 0 dBFS; 0.5 → still clipped on one clip; 0.2 → ≤ −0.3 dBTP, ≤ 0.01 % of samples over −1 dBFS.

### Mix Kit (Editor)

`MixerKit` adds what a mixer is missing and leaves everything it finds as it is: a second run changes nothing, hand edits stay. The mixer is marked dirty; save with `AssetDatabase.SaveAssets()`.

```csharp
AudioMixerGroup master = mixer.FindMatchingGroups("Master")[0];
MixerKit.EnsureLimiter(master, ceilingDb: -1f, releaseMs: 100f, exposedThreshold: "MasterLimit");
AudioMixerGroup music = MixerKit.EnsureGroup(mixer, "Music", "Master");
AudioMixerGroup world = MixerKit.EnsureGroup(mixer, "World", "Master");
MixerKit.EnsureExposedVolume(music, "MusicVolume");
AudioMixerGroup verb = MixerKit.EnsureGroup(mixer, "Reverb", "Master");
MixerKit.EnsureEffect(verb, "SFX Reverb", new Dictionary<string, float> { ["Decay Time"] = 1.2f });
MixerKit.EnsureSend(world, verb, levelDb: -12f);   // Receive heads the Reverb group
AssetDatabase.SaveAssets();
```

Recommended layout: `Master` [limiter] → `Music`, `World` (3D game sounds), `Ambience`, `Voice`, `UI`. The mixer Compressor with attack 0 is an infinite-ratio peak limiter without lookahead (a +20 dB step overshoots ~11 dB for ~15 ms), so keep it a safety: stage levels so it is idle in normal play, and don't put a "glue" compressor on music — it would clamp every transient. The [Output Limiter](#output-limiter) after it catches what it lets through. `exposedThreshold` lets a probe set it to 0 dB (off) and measure gain reduction.

It works through Unity's internal mixer editor API by reflection; when that changes, it logs what it could not make and returns `null`/`false`.

### Output Limiter

The mixer Compressor has no lookahead and reads sample peaks only, so loud sums (several sources close by, thunder over music) still clip. `UnityAudioService` keeps an `OutputLimiter` on the active `AudioListener` (found again within a second when the listener changes) when `AudioServiceConfig.Limiter.Enabled`:

- `OnAudioFilterRead` on the listener gets the whole mix after every mixer group and effect, so it sees everything but the reverb-zone wet (see [Reverb Zones](#reverb-zones)); the ceiling sits below 0 dBTP to leave the wet room;
- true peak: the value between samples by 4× oversampling (16-tap Kaiser-windowed sinc per phase, as BS.1770 meters do; under-reads ≤ 0.15 dB on audio band-limited to 17 kHz at 48 kHz); a block whose interpolation cannot reach the ceiling skips it;
- the gain each sample needs is held over the lookahead and averaged over it, so it ramps down in time and reaches its value exactly as the peak leaves: no sample passes the ceiling; it comes back with an exponential release; channels share one gain;
- everything heard is delayed by the lookahead (1.5 ms + 8 frames by default); the wet is not;
- cost (CoreCLR, loud stereo at 48 kHz): ~0.7 % of a core; quiet blocks ~0.

```csharp
var audio = new UnityAudioService(config);           // config.Limiter: Enabled, CeilingDb -1, LookaheadMs 1.5, ReleaseMs 100
float cut = audio.Limiter?.TakeReductionDb() ?? 0f;  // deepest gain cut since the last ask, dB
audio.LimiterOn = false;                             // A/B; holds for listeners found later
```

`TruePeakLimiter` alone is pure C# for a stream the game owns: `Process(span, channels)` in place, output `Latency` frames late.

A listener-tap `ListenerCapture` records after the limiter only when it was added after it (component order).

### Capture

```csharp
var capture = ListenerCapture.Start("Logs/turn.wav", seconds: 10f);   // Tap.Renderer by default
long at = capture.Frames;                                               // mark events
capture.Written += path => Debug.Log(path);
```

`Tap.Renderer` records the final output, reverb-zone wet included; the device is silent while it records. `Tap.Listener` records on the listener's `OnAudioFilterRead` and is heard live, but misses the reverb-zone wet. Float32 stereo WAV at the output rate. Compiled in the editor and debug builds only.

### Loudness

```csharp
var meter = new LoudnessMeter(rate, channels);                 // BS.1770, gated over a sliding 20 s
var gain = new LoudnessGain(targetLufs: -18f);                 // clamp [-12, +3] dB, down 6 dB/s, up 0.5 dB/s, guard 2 LU
gain.Process(meter, buffer, offset: 0, frames, channels);      // audio thread: measure, step, apply a ramp
float lufs = meter.Integrated, db = gain.Db;                   // main thread may read them
```

For a stream the game feeds itself (music from a decoder): **LoudnessMeter** is ITU-R BS.1770 (K-weighting for any rate, 400 ms blocks every 100 ms, −70 LUFS absolute and −10 LU relative gates) over a sliding window instead of the whole programme; every channel weighs 1. **LoudnessGain** moves toward `target − measured`, clamped, cutting fast and lifting slowly so a quiet intro is not pumped up and a loud part reaches the limiter for a second at most; the first 3 measured seconds slew fast both ways to land the first estimate. The meter also gives `ShortTerm` (the last 3 s, ungated); the gain aims at the louder of the window and short-term − `Guard` (2 LU), so a drop after a quiet intro is cut within a second or two instead of as the window fills, while a chorus up to 2 LU over the track keeps its lift. `Apply` ramps across the buffer, so the gain never steps. Pure C#, no allocation after construction.

### Propagation Delay

```csharp
var delay = new PropagationDelay(rate);                         // slew 0.003, jump 0.05 s, crossfade 0.02 s, max 1 s
delay.Aim(Vector3.Distance(source, ear) / SoundMath.SoundSpeed); // any thread, every frame
// audio thread, per block of `frames` output frames whose undelayed reference starts at `cursor`:
delay.Plan(cursor, frames, out long first, out int count);      // source frames [first, first + count) it will read
delay.Render(window, windowFirst, output, channels);           // window: interleaved source frames from windowFirst
delay.Shift(frames, 0.01f);                                     // the reference cursor resynced: crossfade old → new
```

For several sources playing one programme (speakers on a beach): each lags by its own distance, so the nearest arrives first and leads and the far ones sound as its echo (precedence) instead of a phantom between them. A moving ear changes the delay; `MaxSlew` 0.003 (≈ 5 cents, under the pitch JND of music) caps the read rate, so it is Doppler-like but never a pitch bend; a lag past `Jump` (a vehicle, a teleport) is a raised-cosine crossfade between the old and the new read. `Snap` lands on the aim at the next block (a new track). The caller keeps a history at least `MaxDelay` + the block long; frames outside the window read as silence. Unity's own Doppler stays off on such sources.

### Time

Fades, crossfades and loop ramps run on real time by default (`AudioServiceConfig.UnscaledTime`), so slow motion doesn't stretch them and `Time.timeScale = 0` doesn't freeze them. Turn it off to have them follow the game's time scale.

`Time.timeScale` doesn't touch playback: at `0` sounds play to the end and return to the pool as usual. With `UnscaledTime` off, a zero time scale stops fades mid-way and freezes the `MinInterval` clock, so a repeat inside the window stays dropped until time runs again.

### Mixer Snapshots

```csharp
audio.TransitionToSnapshot("Underwater", duration: 0.5f);
audio.TransitionToSnapshot("Default", duration: 1f);
```

Snapshots must be defined on the AudioMixer asset. A missing snapshot logs a warning and no-ops.

### Volume Control

```csharp
audio.SetVolume("MasterVolume", 0.8f);
audio.SetVolume("VoiceVolume", 0.7f);
float voice = audio.GetVolume("VoiceVolume");   // the mixer's value until set
```

The argument is the name of an exposed mixer parameter. Volumes are clamped to `[0, 1]` and converted to dB (`20·log10(v)`, or `-80 dB` at zero). If a parameter is not exposed on the mixer, a warning is logged. `GetVolume` returns the last value set; for a parameter never set it reads the mixer (`-80 dB` and below is `0`), and returns `1` without a mixer or for a parameter that is not exposed. For ducking under voice, use a Duck Volume effect on the mixer: it doesn't fight the player's volume settings.

### Saving Volumes

The service does not save anything and writes no volume until asked. To keep the player's settings between sessions, the code that owns settings loads them and calls `SetVolume` after construction, and saves them wherever it saves the rest:

```csharp
foreach (var (param, volume) in settings.Volumes)
    audio.SetVolume(param, volume);
```

## Design Decisions

- **The output limiter on the listener, not in the mixer** — a mixer effect needs a native plugin for lookahead; `OnAudioFilterRead` on the listener is managed, gets the whole mix after every group, and costs one DSP for the game. What it cannot see is the reverb-zone wet, so sends are sized to the headroom instead.

- **Separate SoundConfig and MusicConfig** — music must not receive random pitch variation; the type system enforces this rather than documentation.
- **Named loop slots, not handles** — a loop is a semantic slot (`"steps"`, `"ambient"`), not an anonymous instance. Slots are dedicated sources that survive scene loads and are never evicted by the SFX pool, so callers don't manage handles across scenes.
- **A lost target fades its loop out** — a destroyed transform is almost always an object that left the scene, so its loop should end with it; a short fade avoids a click, and a slot the caller stopped is left to its own fade.
- **SFX pool evicts by priority at capacity** — when every source is busy, the lowest priority playing source (oldest among equals) is stopped, its handle invalidated, and it is reused; a sound below every playing one is dropped instead. `AudioSource.priority` is not enough: it only decides which voices Unity mutes, not which one-shot the pool gives up. No allocation spikes at peak concurrency.
- **A fading one-shot gives way first** — a sound stopped with a fade still holds its source until the fade ends. A full pool cuts the quietest of them before any playing sound, so `MaxSfxSources` stays a hard cap after `StopAllSFX(fadeOut)` and the fade costs no live sound.
- **Repeat limits drop the newcomer** — over `MaxInstances` or inside `MinInterval` the new start is dropped rather than cutting a copy already playing, so a burst of contacts can't turn into a stutter of restarts.
- **Volumes by parameter name** — the mixer's layout belongs to the game, so the service takes the exposed parameter name instead of a fixed set of groups.
- **Routing lives in the sound** — `SoundConfig.Output` names the mixer group, so the caller doesn't pick a group per call.
- **No ducking API** — a mixer Duck Volume effect ducks by signal level without code and without writing the same parameter as the player's volume setting.
- **No persistence** — the service plays sound and sets mixer volumes, nothing else. Where settings are saved (PlayerPrefs, a file, a cloud save) belongs to the game, so the package has no dependency on a storage package.
- **Pause lives in the sound** — `PlaysOnPause` sits on `SoundConfig` and `MusicConfig`, like `Output`: a menu sound is a menu sound at every call, and one-shots need it as much as loops, so a flag on the loop slot or a call argument would not do. Mixer groups have no such switch; `ignoreListenerPause` exists only on the source.
- **Silence ends a container on pause** — Unity reports a container played through a paused listener as playing until the pause ends, and gives its length nowhere; the source's output is the only sign that it is over. Clips keep `isPlaying`, which is exact.
- **A paused source is busy** — the pool waits for a sound to end, and `isPlaying` is false under `AudioListener.pause`, so a paused source with a resource still counts as playing; otherwise pausing the game would cut every sound and hand its source to the next one.
- **Real time by default** — a game with slow motion or a paused time scale expects its audio fades to keep their length; following the time scale is the opt-in.
- **Kinds are data, the space is one loop** — a kind's curves are built once and shared; the per-frame part (head shadow, occlusion) is one array walked by `Tick`, not a `MonoBehaviour` per source. 32 ticking sources cost ~20 µs per frame on the main thread and ~0.3 ms/s per low-pass on the audio threads.
- **No HRTF, no spatializer plugin** — front/back is a spectral cue a single native low-pass gives (head shadow); a managed per-ear filter costs 3× the native one. Left/right comes from Unity's panner with a spread that never reaches 0 (hard pan, −∞ dB in the far ear).
- **Rolloff, spread, reverb mix and air as custom curves** — Unity evaluates them on the audio side per source at no main-thread cost; only shadow and occlusion need per-frame writes, and only for kinds that use them.
- **Low-pass Q 0.707** — Unity's default Q 1 bumps +1.25 dB at the cutoff; a moving cutoff on it sounds like a wah.
- **Smoothed shadow and occlusion** — a one-pole on the back amount (60 ms) and on occlusion (attack/release) so a fast turn or a passing rock sweeps the filter instead of stepping it: no clicks, no level jumps.
- **One occlusion ray per source** — to the source's centre, against static world only; a thin plank occludes like a cliff. Several rays and thickness are left for later.
- **Kind-less sources bypass reverb zones** — a zone would otherwise send engines, UI and voice chat into the reverb.
- **The pool's filter is added up front** — every pooled source gets a disabled low-pass when the service has a space, so a play enables it instead of rebuilding the DSP chain of a source about to start.
- **Mixer layout by a helper, not a template** — games already own mixers with their values and exposed names; a template cannot merge into them.
- **Delay by reading the owner's samples, not a filter** — a delay line in `OnAudioFilterRead` needs its own buffer per source; a game that owns the PCM reads it delayed straight from its history. Slewing within 0.3 % instead of resampling to any rate keeps a speaker's pitch steady while the listener walks.
- **3.2.0 is additive** — `IAudioService` is unchanged; nothing to migrate. Sounds without a kind and services without a space behave as in 3.1.
- **Main thread only** — all methods use `AudioSource`, `AudioMixer`, `Time.unscaledDeltaTime` (or `Time.deltaTime`), and `UniTask.Yield`, none of which are thread-safe. Call from the Unity main thread.

## File Structure

```
com.rubickanov.audio/
├── Runtime/
│   ├── Space/       — SoundKind, SoundMath, SoundSpace, KindCurves, PropagationDelay
│   ├── Loudness/    — LoudnessMeter, LoudnessGain
│   ├── Limiter/     — OutputLimiter, TruePeakLimiter
│   └── Capture/     — ListenerCapture
├── Editor/          — MixerKit
└── Tests/           — EditMode (service, math, space, mix kit, loudness, delay) and PlayMode (pause)
```
