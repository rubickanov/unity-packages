# Character Motor

Modular rigidbody-based character motor with pure C# modules. Camera-agnostic — supports FPS, TopDown, and Third-Person setups.

## Quick Start

1. Add `Rigidbody` (dynamic, frozen rotation) + `CapsuleCollider` + `CharacterMotor` to a GameObject.
2. Create a `MotorConfig` asset via **Create > Character Motor > Motor Config**.
3. Implement `IMotorInput` and call `motor.SetInputProvider(yourInput)`.
4. For FPS: also implement `ILookInput` on the same provider and enable **Mouse Look** in Inspector.

## Usage Scenarios

### TopDown

WASD maps to world axes, no camera rotation.

```csharp
var movement = motor.GetModule<MovementModule>()!;
movement.Orientation = MovementOrientation.World;
```

### FPS

Movement relative to camera, MouseLookModule handles body yaw and camera pitch.
Enable **Mouse Look** checkbox on CharacterMotor in Inspector.

```csharp
var movement = motor.GetModule<MovementModule>()!;
movement.SetOrientationSource(cameraTransform);

var mouseLook = motor.GetModule<MouseLookModule>()!;
mouseLook.SetCameraTransform(cameraTransform);
```

### Third-Person

Movement relative to camera pivot, external camera system handles rotation.

```csharp
var movement = motor.GetModule<MovementModule>()!;
movement.SetOrientationSource(cameraPivot);
```

## Modules

| Module | Priority | Description |
|--------|----------|-------------|
| GroundDetectionModule | -100 | SphereCast ground check with slope detection |
| MouseLookModule | -50 | Mouse look with vertical clamp (opt-in) |
| MovementModule | 0 | Translates input into desired velocity |
| SprintModule | 5 | Speed multiplier while grounded + forward |
| JumpModule | 10 | Jump with coyote time and input buffering |
| CrouchModule | 15 | Toggle crouch with smooth height transition |
| StepClimbModule | 20 | Automatic step climbing for small obstacles |
| PhysicsResolverModule | 1000 | Final physics: acceleration, gravity, forces |

## External Forces

Use `IForceReceiver` (implemented by `CharacterMotor`):
- `AddExternalForce(Vector3)` — one-time impulse.
- `SetSpeedModifier(object, float)` / `RemoveSpeedModifier(object)` — persistent multipliers.

## Events

- `CharacterMotor.StateUpdated` — fires every FixedUpdate with `MotorSnapshot`.
- Module-level events: `JumpModule.Jumped`, `JumpModule.Landed`, `GroundDetectionModule.GroundedChanged`, `SprintModule.SprintChanged`, `CrouchModule.CrouchChanged`.
