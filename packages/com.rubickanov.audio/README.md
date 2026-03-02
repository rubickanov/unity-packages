# Audio

Audio service with SFX source pooling, music crossfade, pitch variation, and AudioMixer-based volume control.

## Dependencies

- `UniTask` — async crossfade and source lifecycle

## Architecture

```
IAudioService
├── UnityAudioService    — AudioMixer-based, pooled SFX sources, music crossfade
└── NullAudioService     — no-op for server/headless builds

IAudioPersistence        — optional volume persistence (load/save)
```

**UnityAudioService** takes an **AudioServiceConfig** (ScriptableObject) for mixer groups, pool size, and crossfade duration. If an **IAudioPersistence** is provided, volume levels are persisted and restored automatically.

## Core Concepts

**SoundConfig** — serializable struct wrapping an `AudioResource` and a pitch variation range. Assigned in the Inspector on any MonoBehaviour or ScriptableObject.

**SoundHandle** — opaque readonly struct returned by play methods. Used to stop looping sounds. Fire-and-forget callers ignore it.

## Quick Start

1. Create an `AudioServiceConfig` asset via **Create > Config > Audio Service**. Assign your AudioMixer and mixer groups.
2. Register in your LifetimeScope:

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

// With volume scaling
audioService.PlaySFX(_hitSound, volumeScale: 0.5f);
```

### Looping Sounds

```csharp
[SerializeField] private SoundConfig _footstepLoop = default!;

// Start loop — keep the handle
SoundHandle handle = audioService.PlayLoop(_footstepLoop);

// Stop when done
audioService.StopSound(handle);
```

### Music

```csharp
[SerializeField] private SoundConfig _battleTheme = default!;

// Crossfades from current track (duration from AudioServiceConfig)
audioService.PlayMusic(_battleTheme);

// Stop music
audioService.StopMusic();
```

### Volume Control

```csharp
audioService.SetMasterVolume(0.8f);
audioService.SetMusicVolume(0.5f);
audioService.SetSFXVolume(1f);

// Read current values
float master = audioService.MasterVolume;
```

### Volume Persistence

Implement **IAudioPersistence** and register it alongside the service. **UnityAudioService** will load volumes on construction and save on every `SetXxxVolume()` call.

```csharp
public class StorageAudioPersistence : IAudioPersistence
{
    private readonly IStorageService _storage;

    public StorageAudioPersistence(IStorageService storage) => _storage = storage;

    public float Load(string key, float defaultValue) => _storage.GetFloat(key, defaultValue);
    public void Save(string key, float value) => _storage.SetFloat(key, value).Forget();
}

// Registration
builder.Register<StorageAudioPersistence>(Lifetime.Singleton).As<IAudioPersistence>();
```

## Design Decisions

- **IAudioPersistence instead of IStorageService dependency** — keeps the audio package independent of the storage package. Callers bridge the two in game code.
- **SoundConfig as a struct** — avoids allocations when passed by `in` reference. Wraps `AudioResource` as the future FMOD migration point.
- **SoundHandle for all play methods** — uniform API. One-shot callers ignore the handle; loop callers keep it.
- **Pool evicts oldest source at capacity** — no allocation spikes. When all SFX sources are in use, the oldest playing source is stopped and reused.
