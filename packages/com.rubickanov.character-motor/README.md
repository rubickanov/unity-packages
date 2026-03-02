# Character Motor

Modular character motor with pure C# simulation. Supports Rigidbody and Kinematic (CapsuleCast) bodies, camera-agnostic orientation (FPS, TopDown, Third-Person), and state snapshots for multiplayer prediction/reconciliation.

## Dependencies

None.

## Architecture

```
CharacterMotor (MonoBehaviour shell)
        │
        ▼
MotorSimulation (pure C#, owns state + modules)
        │
        ├── IMotorBody
        │   ├── RigidbodyMotorBody    — Unity physics (singleplayer)
        │   └── KinematicMotorBody    — CapsuleCast sweeps (multiplayer)
        │
        └── IMotorModule (sorted by Priority)
            ├── GroundDetectionModule  (-100)
            ├── MouseLookModule        (-50)
            ├── MovementModule         (0)
            ├── SprintModule           (5)
            ├── JumpModule             (10)
            ├── CrouchModule           (15)
            ├── StepClimbModule        (20)
            ├── SlopeSlideModule       (25)
            └── PhysicsResolverModule  (1000)
```

**CharacterMotor** is a thin MonoBehaviour that bridges Unity lifecycle to **MotorSimulation**. In singleplayer it auto-ticks in FixedUpdate; in multiplayer, set `AutoSimulate` to false and call `Simulate()` manually.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Motor.Runtime** | Yes | Simulation, bodies, modules, data types |
| **Motor.Editor** | Editor | Custom inspector for CharacterMotor |

## Core Concepts

**MotorSimulation** — Pure C# simulation runner. Holds shared **MotorState**, an ordered list of modules, and a body reference. Can be ticked from FixedUpdate or a network tick system.

**IMotorBody** — Physics abstraction. **RigidbodyMotorBody** delegates to Unity physics (singleplayer). **KinematicMotorBody** resolves collisions via iterative CapsuleCast sweeps (deterministic, multiplayer-ready).

**IMotorModule** — Single unit of motor behavior. Modules read/write shared **MotorState** and interact with the body. Executed in `Priority` order (lower runs first). Added via `[SerializeReference]` list on **CharacterMotor**.

**IStatefulModule** — Optional interface for modules with persistent internal state that must be saved/restored for prediction and reconciliation.

## Quick Start

1. Add `CapsuleCollider` + **CharacterMotor** to a GameObject.
2. In the Inspector, choose body type (Kinematic or Rigidbody) and add modules via the "Add Module" button.
3. Implement **IMotorInputProvider** and wire it up.

```csharp
public class PlayerInput : MonoBehaviour, IMotorInputProvider
{
    public Vector2 MoveInput => inputActions.Move.ReadValue<Vector2>();
    public bool JumpPressed => inputActions.Jump.WasPressedThisFrame();
    public bool SprintHeld => inputActions.Sprint.IsPressed();
    public bool CrouchPressed => inputActions.Crouch.WasPressedThisFrame();

    private void Start()
    {
        GetComponent<CharacterMotor>().SetInputProvider(this);
    }
}
```

## Usage

### Singleplayer (Auto-Tick)

Set an input provider and let **CharacterMotor** tick automatically in FixedUpdate:

```csharp
motor.SetInputProvider(playerInput);
```

### Multiplayer (Manual Tick)

Disable auto-simulation and call `Simulate()` from your network tick:

```csharp
motor.AutoSimulate = false;

// Each network tick:
var input = new MotorInput
{
    Move = networkInput.Move,
    Jump = networkInput.Jump,
    Sprint = networkInput.Sprint
};
motor.Simulate(input, NetworkManager.ServerTime.FixedDeltaTime);
```

### Movement Orientation

**TopDown** — WASD maps to world axes:

```csharp
var movement = motor.GetModule<MovementModule>()!;
movement.Orientation = MovementOrientation.World;
```

**FPS / Third-Person** — movement relative to a camera transform:

```csharp
var movement = motor.GetModule<MovementModule>()!;
movement.Orientation = MovementOrientation.Transform;
movement.SetOrientationSource(cameraTransform);
```

### Mouse Look (FPS)

```csharp
var mouseLook = motor.GetModule<MouseLookModule>()!;
mouseLook.SetLookInputProvider(() => inputActions.Look.ReadValue<Vector2>());
mouseLook.SetCameraTransform(cameraTransform);
```

### External Forces

**CharacterMotor** implements **IForceReceiver**:

```csharp
IForceReceiver receiver = motor;
receiver.AddExternalForce(explosionDir * 15f);
receiver.SetSpeedModifier(slowDebuff, 0.5f);
receiver.RemoveSpeedModifier(slowDebuff);
```

### State Events

```csharp
motor.StateUpdated += snapshot =>
{
    animator.SetFloat("Speed", snapshot.HorizontalSpeed);
    animator.SetBool("Grounded", snapshot.IsGrounded);
};

motor.GetModule<JumpModule>()!.Jumped += force => PlayJumpSFX();
motor.GetModule<JumpModule>()!.Landed += velocity => PlayLandSFX(velocity);
motor.GetModule<GroundDetectionModule>()!.GroundedChanged += grounded => { };
```

### Prediction and Reconciliation

```csharp
// Save state before prediction
MotorStateSnapshot snapshot = motor.Simulation.SaveState();

// Re-simulate from authoritative state
motor.Simulation.RestoreState(serverSnapshot);
motor.Simulate(replayInput, fixedDeltaTime);
```

### Custom Modules

Extend **MotorModuleBase** and add via Inspector or `Simulation.AddModule()`:

```csharp
[Serializable]
public class DashModule : MotorModuleBase, IStatefulModule
{
    [SerializeField] private float _dashSpeed = 20f;
    [SerializeField] private float _dashDuration = 0.15f;

    public override int Priority => 12;

    private float _timer;

    public override void Simulate(float deltaTime)
    {
        if (_timer > 0f)
        {
            State.SkipDefaultPhysics = true;
            Body.AddForce(Body.Transform.forward * _dashSpeed, ForceMode.VelocityChange);
            _timer -= deltaTime;
        }
    }

    public void SaveState(ref ModuleStateWriter writer) => writer.Write(_timer);
    public void RestoreState(ref ModuleStateReader reader) => _timer = reader.ReadFloat();
}
```

### Input Extensions

For module-specific input beyond Move/Jump/Sprint/Crouch, use **InputExtensions**:

```csharp
var extensions = new InputExtensions();
extensions.Set(new LookInputData { Look = mouseDelta });

motor.SetInputExtensionsProvider(() => extensions);
```

## Built-in Modules

| Module | Priority | Description |
|--------|----------|-------------|
| **GroundDetectionModule** | -100 | SphereCast ground check with slope angle detection |
| **MouseLookModule** | -50 | Mouse look with vertical clamp and optional deterministic yaw |
| **MovementModule** | 0 | Translates input into desired velocity (World or Transform orientation) |
| **SprintModule** | 5 | Speed multiplier while grounded, moving forward, not crouching |
| **JumpModule** | 10 | Jump with coyote time and input buffering |
| **CrouchModule** | 15 | Toggle crouch with smooth height transition and ceiling detection |
| **StepClimbModule** | 20 | Automatic step climbing for small obstacles |
| **SlopeSlideModule** | 25 | Downward sliding force on steep slopes |
| **PhysicsResolverModule** | 1000 | Final physics: acceleration, gravity, air control, external forces |

## Design Decisions

- **Pure C# simulation** — **MotorSimulation** has no MonoBehaviour dependency. This makes it testable and allows manual ticking for multiplayer prediction loops.
- **MotorInput struct instead of IMotorInput interface** — Serializable value type that can be sent over the network. **IMotorInputProvider** is a convenience for singleplayer auto-tick only.
- **Module config via [SerializeField] on each module** — No monolithic MotorConfig ScriptableObject. Each module carries its own tuning parameters, added/removed independently.
- **KinematicMotorBody for multiplayer** — CapsuleCast sweeps are deterministic across frames, unlike Rigidbody which depends on Unity's physics solver state.
