# Character

A third-person character body for co-op games: a **CharacterMotor** over Kinematic Character Controller, a
**CharacterAnimator** on Animancer, a **CharacterRagdoll** that falls, lies and gets up, and a **CharacterRig** that
plants the feet, turns the head, leans into turns and puts hands and feet on seats. One copy for several games: each
game turns features on through hooks and its own config assets.

## Dependencies

The package lists no dependencies in `package.json` (UPM cannot depend on git packages, and two of these are Asset
Store code that must not be published). The game provides:

| Assembly (by name) | What | Where in the game |
|---|---|---|
| `KCC` | Kinematic Character Controller (Asset Store) | e.g. `Assets/Plugins/KCC` with its own asmdef |
| `Kybernetik.Animancer` | Animancer (Asset Store) | e.g. an embedded `Packages/com.kybernetik.animancer` |
| `Rubickanov.Log.Runtime` | `com.rubickanov.log` | the game's `Packages/manifest.json` |

Input System is not needed: the game reads its own input and hands the character a `CharacterInput`. The package is
not in `unity-project-pckgs` (it does not compile without KCC and Animancer); its tests run from a game.

## Architecture

```text
input (game) ──► CharacterMotor ──► CharacterMotion ──► CharacterAnimator (Animancer)
                 (KCC motor:                              │
                  walk, sprint, jump,                     ▼
                  swim, follow)                    CharacterRig (LateUpdate)
                      ▲                             FootPlacement, HeadLook, TurnLean,
                      │ Suspend / Follow            MountLimbs (seat + work hands), WorkBodyReach
                 CharacterRagdoll ──► Ragdoll (bones, pins, push) + GetUpContacts
                 CharacterClimb (IClimbable: ClimbPoint, game edges)
```

The motor owns the capsule. A fall suspends it: the ragdoll takes over, the capsule follows the hips upright, and the
get-up hands the body back.

## Assemblies

| Assembly | References | Contents |
|---|---|---|
| `Rubickanov.Rig.Runtime` | UnityEngine only | Math: `TwoBoneIK`, `Spring`, `AimAngles`, `TurnOffset`, `LimbPin`, `SoleContact`, `RagdollMath` |
| `Rubickanov.Characters.Runtime` | `Rubickanov.Rig.Runtime`, `KCC`, `Kybernetik.Animancer`, `Rubickanov.Log.Runtime` | Motor, animator, ragdoll, rig, climbing, configs, hooks |
| `Rubickanov.Rig.Tests` | `Rubickanov.Rig.Runtime`, NUnit | EditMode tests of the Rig math |

## Layout

One namespace per assembly (`Rubickanov.Characters`, `Rubickanov.Rig`); folders only group files.

```text
Runtime/
├── Rig/                    math, no components
└── Characters/
    ├── CharacterProportions.cs
    ├── Motor/              CharacterMotor, input, motion, MovementConfig, MovementLimits
    ├── Animation/          CharacterAnimator, CharacterAnimationsConfig, HandShape
    │   └── IK/             CharacterRig + config, FootPlacement, HeadLook, TurnLean, GetUpContacts, MountLimbs
    ├── Ragdoll/            CharacterRagdoll, Ragdoll, RagdollBone, RagdollConfig
    ├── Climb/              CharacterClimb, IClimbable, ClimbPoint
    ├── WorkHands/          IWorkHands, leases, hand and body reach
    └── Hooks/              what the game implements: input source, mount, water, foot ground, footprints, solid
Tests/Rig/
```

## Core Concepts

Every tunable lives in a config asset the game owns: **MovementConfig** (speeds, jump, swimming), **CharacterRigConfig**
(feet, head look, lean, seat grips, hand shapes), **CharacterAnimationsConfig** (clips), **RagdollConfig** (muscles,
falling, get-up, water), **CharacterProportions** (build data for a game's character builder).

A feature is off until the game turns it on: swimming needs a `Water`, footprints an `IFootprints`, work hands an
`IWorkHands`. Three config fields are where games chose differently; their defaults are the conservative side:

| Field | Default | Other value |
|---|---|---|
| `RagdollConfig.FallTime` | `0`: a long fall stays upright until it lands | `1`: after 1 s of falling the body goes down into its ragdoll in the air |
| `RagdollConfig.SolverVelocityIterations` | `1` (the physics default) | e.g. `4`: stiffer bones on a moving deck |
| `CharacterRigConfig.MountGrip` | `Wrist`: the wrist goes onto the hold, the hand keeps the clip's turn | `Palm`: the palm lies on the hold, fingers shaped Fist/OpenPalm |

## Quick Start

1. Character prefab: `KinematicCharacterMotor` + **CharacterMotor** (with a MovementConfig) on the root, the model with
   an `Animator` + `AnimancerComponent` + **CharacterAnimator**, **CharacterRig** and **CharacterRagdoll** with their
   configs.
2. Feed input each frame, by push or by pull:

```csharp
motor.SetInput(new CharacterInput
{
    Move = cameraYaw * new Vector3(move.x, 0f, move.y),
    Look = cameraForward,
    Aim = cameraForward,      // world direction the head looks at
    Sprint = sprintHeld,
    JumpPressed = jumpPressed,
});

motor.InputSource = playerInput;   // or: an ICharacterInputSource read in the motor's Update
```

## Usage

Game-specific behaviour enters through these hooks; nothing in the package names a game type.

| Hook | Set on | What it does |
|---|---|---|
| `ICharacterInputSource` | `CharacterMotor.InputSource` | Motor pulls a `CharacterInput` every frame |
| `ICharacterWater` | `CharacterMotor.Water` | Water height and flow: swimming, floating ragdoll, swim-up |
| `IFootGround` | `CharacterRig.FootGround` / `BodyGround` | Ground the feet and the lying body see (snow, sand) |
| `IFootprints` | `CharacterRig.Footprints` | Told each time a foot is planted |
| `ICharacterSolid` | component on a dynamic body | `StopsCharacters`: the capsule stops at it and does not stand on it |
| `ICharacterMount`, `ClimbPoint` | `CharacterMotor.Mount` | Seat anchor, pose, hand/foot holds, hand shapes |
| `IClimbable` | `Climbables.Add` / `Remove` | Answers a swimmer's `ClimbQuery` with a `ClimbEdge` to pull up onto (`ClimbPoint` is one; reach over the shoulders is `MovementConfig.ClimbReachUp`) |
| `IWorkHands`, `WorkHandsLease` | `CharacterRig.WorkHands` | Hands reach for a work station's targets; one owner at a time |
| `MovementConstraints` | `CharacterMotor.Constraints.Add(limits)` | Caps speed, sprint, jump, turning while the lease lives |
| `Carrier` | `CharacterRagdoll.Carrier` | Velocity of what the lying body rests on (a moving deck) |
| `SpeedMultiplier`, `JumpScale`, `GravityOverride` | `CharacterMotor` | Per-game scaling of the walk, jump and gravity |

```csharp
// A work station holds the character still and takes its hands while used.
using IDisposable hold = motor.Constraints.Add(new MovementLimits(0f, sprint: false, jump: false, turnBody: false));
using WorkHandsLease hands = WorkHandsLease.TryAcquire(rig, station);

// Knocked down by a hit, or thrown off a vehicle with the vehicle's velocity.
ragdoll.Knockdown(impulse, hitPoint);
ragdoll.Throw(seatVelocity + Vector3.up * 2f);

// What the player asked for this step, before walls and slopes (e.g. to push a ball).
Vector3 wanted = motor.WantedVelocity;
```

## Integration

What stays in each game: reading its own input and camera, network snapshots of the body and the ragdoll, interaction
and items, the builders that make the character prefab and config assets, and the assets themselves (clips, configs,
prefabs). Script GUIDs in this package are the ones the games had, so prefabs keep their references.

Working on the package from a game: `./link.sh <game> local com.rubickanov.character` (absolute `file:` path and
`testables`), fix and check in the game, commit and push here, then `./link.sh <game> pin <sha>` in each game.

## Design Decisions

- **One copy for several games.** The games drifted apart while each kept its own copy; a fix is now made once.
- **Off unless the game asks.** A feature one game uses (swimming, the in-air ragdoll, palm grips, work hands) sits in
  the package but changes nothing in a game that sets no hook and keeps the config defaults.
- **No Asset Store code.** KCC and Animancer are referenced by assembly name only.
