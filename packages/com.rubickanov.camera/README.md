# com.rubickanov.camera

Camera service with Cinemachine v3 follow, aim offset, and impulse-based screen shake.

## Architecture

```
ICameraService
├── CinemachineCameraService   — Cinemachine v3 follow + aim offset + impulse shake
└── NullCameraService          — no-op for server/headless builds
```

## Key Types

| Type | Description |
|------|-------------|
| `ICameraService` | Interface for camera follow, aim offset, and screen shake |
| `CinemachineCameraService` | Cinemachine v3 implementation with proxy tracking and impulse |
| `CameraConfig` | ScriptableObject config (aim weights, dead zone, shake force) |
| `NullCameraService` | No-op implementation for server builds |

## Usage

```csharp
// Register in DI container
builder.Register<CinemachineCameraService>(Lifetime.Singleton)
    .As<ICameraService>()
    .As<ILateTickable>();

// Follow a target
cameraService.SetFollowTarget(playerTransform);

// Aim offset (mouse look-ahead)
cameraService.SetAimOffset(offset);

// Screen shake
cameraService.Shake(direction, force);
```
