# Audio

Audio service with SFX source pooling, music crossfade, ducking, fade in/out, mixer snapshots, and persistent volumes.

## Dependencies

- `com.rubickanov.storage` — volume persistence via `IStorageService`
- `UniTask` — async crossfade, fades, ducking, source lifecycle

## Architecture

```
IAudioService
├── UnityAudioService    — AudioMixer-based, pooled SFX sources, named loop slots, music crossfade
└── NullAudioService     — no-op for server/headless builds
```

**UnityAudioService** takes an **AudioServiceConfig** (ScriptableObject) for the mixer, its groups, exposed parameter names, pool size, and default crossfade duration. An optional **IStorageService** persists and restores master/music/SFX volumes.

## Core Concepts

**SoundConfig** — struct with an `AudioResource` and pitch variation range (0–0.5). Used for SFX and loops. Assigned in the Inspector.

**MusicConfig** — struct with only an `AudioResource`. No pitch variation. Used for `PlayMusic`.

**SoundHandle** — opaque readonly struct returned by `PlaySFX*`. Pass to `StopSound` to stop the one-shot early. Ignore if you don't need it.

**Loop slot** — a string key identifying a dedicated, non-pooled `AudioSource`. `PlayLoop("steps", cfg)` reuses the same slot across calls and never gets evicted by the SFX pool.

## Quick Start

1. Create an `AudioServiceConfig` asset via **Create > Config > Audio Service**. Assign the mixer, its groups, and the names of exposed parameters for master/music/SFX volume.
2. Register in your DI container:

```csharp
builder.RegisterInstance(audioConfig);
builder.Register<UnityAudioService>(Lifetime.Singleton).As<IAudioService>();
```

## Usage

### Playing SFX

```csharp
[SerializeField] private SoundConfig _hitSound = default!;

audioService.PlaySFX(_hitSound);
audioService.PlaySFXAtPoint(_hitSound, enemy.transform.position);
audioService.PlaySFXAttached(_hitSound, enemy.transform);

audioService.PlaySFX(_hitSound, volumeScale: 0.5f);
audioService.PlaySFX(_hitSound, fadeIn: 0.2f);
```

### Stopping SFX

```csharp
SoundHandle handle = audioService.PlaySFX(_alarmSound);
audioService.StopSound(handle);                 // immediate
audioService.StopSound(handle, fadeOut: 0.3f);  // fade then release
```

### Loops via Named Slots

```csharp
[SerializeField] private SoundConfig _footsteps = default!;
[SerializeField] private SoundConfig _wind = default!;

audioService.PlayLoop("steps", _footsteps);
audioService.PlayLoop("ambient", _wind, fadeIn: 1f);

if (audioService.IsLoopPlaying("steps"))
    audioService.StopLoop("steps", fadeOut: 0.2f);
```

Calling `PlayLoop` on a live slot replaces the current sound on the same source.

### Music

```csharp
[SerializeField] private MusicConfig _battleTheme = default!;

audioService.PlayMusic(_battleTheme);                          // default crossfade
audioService.PlayMusic(_battleTheme, crossfadeDuration: 3f);   // per-call override
audioService.StopMusic();
```

Switching tracks mid-crossfade cancels the in-flight transition and starts a new one from the current outgoing volume — no volume snaps.

### Ducking

Temporarily reduce SFX volume (e.g., during VO or cinematic beats) without touching the user-set SFX volume:

```csharp
// Duck to 30% for 2s with 50ms attack and 300ms release
audioService.DuckSFX(amount01: 0.3f, duration: 2f, attack: 0.05f, release: 0.3f);
```

### Mixer Snapshots

```csharp
audioService.TransitionToSnapshot("Underwater", duration: 0.5f);
audioService.TransitionToSnapshot("Default", duration: 1f);
```

Snapshots must be defined on the AudioMixer asset. Missing snapshots log a warning and no-op.

### Volume Control

```csharp
audioService.SetMasterVolume(0.8f);
audioService.SetMusicVolume(0.5f);
audioService.SetSFXVolume(1f);

float master = audioService.MasterVolume;
```

Volumes are clamped to `[0, 1]` and converted to dB via `20·log10(v)` before being written to the exposed mixer parameters named in `AudioServiceConfig`. If an exposed parameter is missing, a warning is logged.

### Volume Persistence

Provide an `IStorageService`. Volumes hydrate on construction and save on every setter call.

```csharp
builder.Register<IStorageService, PlayerPrefsStorageService>(Lifetime.Singleton);
builder.RegisterInstance(audioConfig);
builder.Register<IAudioService, UnityAudioService>(Lifetime.Singleton);
```

## Thread Safety

All public methods must be called from the Unity main thread. The service uses `AudioSource`, `AudioMixer`, `Time.deltaTime`, and `UniTask.Yield` — none of which are thread-safe.

## Design Decisions

- **Separate SoundConfig and MusicConfig** — music must not get random pitch variation; the type system enforces this rather than documentation.
- **Named loop slots, not handles** — a loop is a semantic slot (`"steps"`, `"ambient"`), not an anonymous instance. Slots are dedicated sources that never get evicted by the SFX pool, and callers don't have to manage handles across scenes.
- **SFX pool evicts oldest at capacity** — no allocation spikes. When all SFX sources are in use, the oldest playing source is stopped, its handle is invalidated, and it is reused.
- **Mixer parameter names in config** — `MasterVolumeParam`, `MusicVolumeParam`, `SfxVolumeParam` (plus optional UI/Dialog/Ambient) are fields on `AudioServiceConfig`. No hardcoded names.
- **Persistence is opt-in** — without an `IStorageService`, volumes reset each session.
- **Ducking goes through the mixer, not per-source** — one parameter write instead of touching every active SFX source.
