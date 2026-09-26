# Audio

Audio service with SFX source pooling (priority eviction, per-sound repeat limits), loop slots with live volume and pitch, music crossfade, fade in/out, mixer snapshots, and mixer volume control.

## Dependencies

> `UniTask` comes from a git URL, not from UPM — UPM will not pull it in for you. See [Third-party dependencies](https://github.com/rubickanov/unity-packages#third-party-dependencies).

- `UniTask` — async crossfade, fades, ramps, and source lifecycle watchers

Requires Unity 6000.0 or newer (uses `AudioResource` and `AudioSource.resource`).

## Architecture

```
IAudioService
├── UnityAudioService    — AudioMixer-based, pooled SFX sources, named loop slots, music crossfade
└── NullAudioService     — no-op for server/headless builds
```

**UnityAudioService** is constructed from an **AudioServiceConfig** (ScriptableObject) that supplies the mixer, the music and default SFX groups, pool size, default crossfade duration, 3D distance settings, and whether fades run on real time. The service creates a `[AudioService]` GameObject (marked `DontDestroyOnLoad` in play mode) that hosts the music sources, the SFX pool, and loop sources.

## Core Concepts

**SoundConfig** — Serializable struct with an `AudioResource`, a pitch variation range (`0`–`0.5`), an optional output `AudioMixerGroup`, and pool rules for one-shots: `Priority`, `MaxInstances`, `MinInterval`. Used for SFX and loops. Assigned in the Inspector. A sound with no output plays through the config's `SfxGroup`; set one to route crowd, UI or voice sounds to their own group.

**MusicConfig** — Serializable struct with only an `AudioResource`. No pitch variation. Used by `PlayMusic`.

**SoundHandle** — Opaque readonly struct returned by `PlaySFX*`. Pass it to `StopSound` to stop a one-shot early. Ignore it if you don't need to.

**Loop slot** — A string key identifying a dedicated, non-pooled `AudioSource`. `PlayLoop("steps", cfg)` reuses the same slot across calls and is never evicted by the SFX pool.

## Quick Start

1. Create an `AudioServiceConfig` asset via **Create > Config > Audio Service**. Assign the mixer, the music group and the default SFX group; set the 3D distances to the range your camera hears from.
2. Construct the service:

```csharp
IAudioService audio = new UnityAudioService(audioConfig);
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
audio.StopSound(handle, fadeOut: 0.3f);  // fade out, then release to the pool

audio.StopAllSFX();                      // every pooled one-shot; loops and music keep playing
audio.StopAllSFX(fadeOut: 0.2f);
```

### Priority and Repeat Limits

Set in the Inspector on each `SoundConfig`; zeros mean no rule, so a new sound behaves like a plain pooled one-shot.

| Field | Effect |
|---|---|
| `Priority` | When the pool is full, the playing one-shot with the lowest priority is evicted, oldest first. A sound whose priority is below every playing one is dropped. |
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

Calling `PlayLoop` on a live slot replaces the current sound on the same source.

Change a live loop without restarting it:

```csharp
audio.SetLoopVolume("crowd", 0.9f, duration: 0.5f);   // ramp to 0.9
audio.SetLoopPitch("wind", 1.3f, duration: 0.2f);     // absolute pitch, not a multiplier
```

Both are no-ops on a slot that is stopped or fading out, so a late update can't bring back a loop you stopped.

### Music

```csharp
[SerializeField] private MusicConfig _battleTheme;

audio.PlayMusic(_battleTheme);                          // default crossfade
audio.PlayMusic(_battleTheme, crossfadeDuration: 3f);   // per-call override
audio.StopMusic();
```

Music uses two alternating sources. Switching tracks mid-crossfade cancels the in-flight transition and starts a new one from the current outgoing volume, so volumes never snap.

### Time

Fades, crossfades and loop ramps run on real time by default (`AudioServiceConfig.UnscaledTime`), so slow motion doesn't stretch them and `Time.timeScale = 0` doesn't freeze them. Turn it off to have them follow the game's time scale.

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
float voice = audio.GetVolume("VoiceVolume");   // 1 until set
```

The argument is the name of an exposed mixer parameter. Volumes are clamped to `[0, 1]` and converted to dB (`20·log10(v)`, or `-80 dB` at zero). If a parameter is not exposed on the mixer, a warning is logged. For ducking under voice, use a Duck Volume effect on the mixer: it doesn't fight the player's volume settings.

### Saving Volumes

The service does not save anything and writes no volume until asked. To keep the player's settings between sessions, the code that owns settings loads them and calls `SetVolume` after construction, and saves them wherever it saves the rest:

```csharp
foreach (var (param, volume) in settings.Volumes)
    audio.SetVolume(param, volume);
```

## Design Decisions

- **Separate SoundConfig and MusicConfig** — music must not receive random pitch variation; the type system enforces this rather than documentation.
- **Named loop slots, not handles** — a loop is a semantic slot (`"steps"`, `"ambient"`), not an anonymous instance. Slots are dedicated sources that survive scene loads and are never evicted by the SFX pool, so callers don't manage handles across scenes.
- **SFX pool evicts by priority at capacity** — when every source is busy, the lowest priority playing source (oldest among equals) is stopped, its handle invalidated, and it is reused; a sound below every playing one is dropped instead. `AudioSource.priority` is not enough: it only decides which voices Unity mutes, not which one-shot the pool gives up. No allocation spikes at peak concurrency.
- **Repeat limits drop the newcomer** — over `MaxInstances` or inside `MinInterval` the new start is dropped rather than cutting a copy already playing, so a burst of contacts can't turn into a stutter of restarts.
- **Volumes by parameter name** — the mixer's layout belongs to the game, so the service takes the exposed parameter name instead of a fixed set of groups.
- **Routing lives in the sound** — `SoundConfig.Output` names the mixer group, so the caller doesn't pick a group per call.
- **No ducking API** — a mixer Duck Volume effect ducks by signal level without code and without writing the same parameter as the player's volume setting.
- **No persistence** — the service plays sound and sets mixer volumes, nothing else. Where settings are saved (PlayerPrefs, a file, a cloud save) belongs to the game, so the package has no dependency on a storage package.
- **Real time by default** — a game with slow motion or a paused time scale expects its audio fades to keep their length; following the time scale is the opt-in.
- **Main thread only** — all methods use `AudioSource`, `AudioMixer`, `Time.unscaledDeltaTime` (or `Time.deltaTime`), and `UniTask.Yield`, none of which are thread-safe. Call from the Unity main thread.
