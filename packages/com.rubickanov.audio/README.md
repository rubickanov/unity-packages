# com.rubickanov.audio

Audio service with SFX pooling, music crossfade, pitch variation, and AudioMixer-based volume control.

## Architecture

```
IAudioService
├── UnityAudioService   — AudioMixer-based impl with SFX source pooling
└── NullAudioService    — no-op for server/headless builds

IAudioPersistence       — optional volume persistence (load/save)
SoundConfig             — serializable struct wrapping AudioResource + pitch variation
SoundHandle             — opaque handle for controlling playing sounds
```

## Key Types

| Type | Description |
|------|-------------|
| `IAudioService` | Interface for audio playback and volume control |
| `UnityAudioService` | AudioMixer implementation with pooled SFX sources and music crossfade |
| `IAudioPersistence` | Optional interface for persisting volume settings |
| `AudioServiceConfig` | ScriptableObject config (mixer groups, pool size, crossfade duration) |
| `NullAudioService` | No-op implementation for server builds |
| `SoundConfig` | Serializable struct bundling AudioResource + pitch variation |
| `SoundHandle` | Opaque handle returned by Play methods for stopping sounds |

## Usage

```csharp
// Register in DI container
builder.Register<UnityAudioService>(Lifetime.Singleton).As<IAudioService>();

// Optional: register persistence for volume settings
builder.Register<MyAudioPersistence>(Lifetime.Singleton).As<IAudioPersistence>();

// Play SFX (fire-and-forget)
audioService.PlaySFX(soundConfig);
audioService.PlaySFXAtPoint(soundConfig, position);

// Loop SFX (footsteps, ambience)
var handle = audioService.PlayLoop(loopConfig);
audioService.StopSound(handle);

// Music with crossfade
audioService.PlayMusic(musicConfig);

// Volume control (persisted if IAudioPersistence is registered)
audioService.SetMasterVolume(0.8f);
```
