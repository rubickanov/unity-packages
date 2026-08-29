# Audio

Audio service with SFX source pooling, music crossfade, ducking, fade in/out, mixer snapshots, and persistent volumes.

## Dependencies

> `UniTask` comes from a git URL, not from UPM — UPM will not pull it in for you. See [Third-party dependencies](https://github.com/rubickanov/unity-packages#third-party-dependencies).

- `com.rubickanov.storage` — optional volume persistence via `IStorageService`
- `UniTask` — async crossfade, fades, ducking, and source lifecycle watchers

Requires Unity 6000.0 or newer (uses `AudioResource` and `AudioSource.resource`).

## Architecture

```
IAudioService
├── UnityAudioService    — AudioMixer-based, pooled SFX sources, named loop slots, music crossfade
└── NullAudioService     — no-op for server/headless builds
```

**UnityAudioService** is constructed from an **AudioServiceConfig** (ScriptableObject) that supplies the mixer, its groups, exposed parameter names, pool size, and default crossfade duration. An optional **IStorageService** persists and restores master/music/SFX volumes. The service creates a `[AudioService]` GameObject (marked `DontDestroyOnLoad` in play mode) that hosts the music sources, the SFX pool, and loop sources.

## Core Concepts

**SoundConfig** — Serializable struct with an `AudioResource` and a pitch variation range (`0`–`0.5`). Used for SFX and loops. Assigned in the Inspector.

**MusicConfig** — Serializable struct with only an `AudioResource`. No pitch variation. Used by `PlayMusic`.

**SoundHandle** — Opaque readonly struct returned by `PlaySFX*`. Pass it to `StopSound` to stop a one-shot early. Ignore it if you don't need to.

**Loop slot** — A string key identifying a dedicated, non-pooled `AudioSource`. `PlayLoop("steps", cfg)` reuses the same slot across calls and is never evicted by the SFX pool.

## Quick Start

1. Create an `AudioServiceConfig` asset via **Create > Config > Audio Service**. Assign the mixer, its groups, and the names of the exposed parameters for master/music/SFX volume.
2. Construct the service, optionally passing an `IStorageService` for persistence:

```csharp
IAudioService audio = new UnityAudioService(audioConfig, storage);
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

A one-shot returns its source to the pool automatically when it finishes. An invalid `SoundConfig` (no resource) plays nothing and returns `SoundHandle.Invalid`.

### Stopping SFX

```csharp
SoundHandle handle = audio.PlaySFX(_alarmSound);
audio.StopSound(handle);                 // immediate
audio.StopSound(handle, fadeOut: 0.3f);  // fade out, then release to the pool
```

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

### Music

```csharp
[SerializeField] private MusicConfig _battleTheme;

audio.PlayMusic(_battleTheme);                          // default crossfade
audio.PlayMusic(_battleTheme, crossfadeDuration: 3f);   // per-call override
audio.StopMusic();
```

Music uses two alternating sources. Switching tracks mid-crossfade cancels the in-flight transition and starts a new one from the current outgoing volume, so volumes never snap.

### Ducking

Temporarily attenuate SFX (e.g., during VO or cinematic beats) without changing the user-set SFX volume:

```csharp
// Duck to 30% of the current SFX volume for 2s, 50ms attack, 300ms release.
audio.DuckSFX(amount01: 0.3f, duration: 2f, attack: 0.05f, release: 0.3f);
```

A new `DuckSFX` call supersedes any duck in progress. Ducking is applied through the SFX mixer parameter, so it costs one parameter write rather than touching every active source.

### Mixer Snapshots

```csharp
audio.TransitionToSnapshot("Underwater", duration: 0.5f);
audio.TransitionToSnapshot("Default", duration: 1f);
```

Snapshots must be defined on the AudioMixer asset. A missing snapshot logs a warning and no-ops.

### Volume Control

```csharp
audio.SetMasterVolume(0.8f);
audio.SetMusicVolume(0.5f);
audio.SetSFXVolume(1f);

float master = audio.MasterVolume;   // also MusicVolume, SFXVolume
```

Volumes are clamped to `[0, 1]` and converted to dB (`20·log10(v)`, or `-80 dB` at zero) before being written to the exposed mixer parameters named in `AudioServiceConfig`. If a parameter is not exposed on the mixer, a warning is logged.

### Volume Persistence

Pass an `IStorageService` to the constructor. Volumes hydrate from storage on construction and save on every setter call. Without it, volumes reset each session.

```csharp
IStorageService storage = new PlayerPrefsStorageService();
IAudioService audio = new UnityAudioService(audioConfig, storage);
```

## Design Decisions

- **Separate SoundConfig and MusicConfig** — music must not receive random pitch variation; the type system enforces this rather than documentation.
- **Named loop slots, not handles** — a loop is a semantic slot (`"steps"`, `"ambient"`), not an anonymous instance. Slots are dedicated sources that survive scene loads and are never evicted by the SFX pool, so callers don't manage handles across scenes.
- **SFX pool evicts oldest at capacity** — when every source is busy, the oldest playing source is stopped, its handle invalidated, and it is reused. No allocation spikes at peak concurrency.
- **Mixer parameter names live in config** — `MasterVolumeParam`, `MusicVolumeParam`, `SfxVolumeParam` (plus optional UI/Dialog/Ambient) are fields on `AudioServiceConfig`. No hardcoded names.
- **Persistence is opt-in** — the service works without an `IStorageService`; passing one is the only thing that makes volumes survive sessions.
- **Main thread only** — all methods use `AudioSource`, `AudioMixer`, `Time.deltaTime`, and `UniTask.Yield`, none of which are thread-safe. Call from the Unity main thread.
