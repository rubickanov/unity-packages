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

Input System is not needed: the game reads its own input and hands the character a `CharacterInput`. Netcode for
GameObjects is optional: with it the `Rubickanov.Characters.Netcode` module compiles (see Netcode (optional)). The package is
not in `unity-project-pckgs` (it does not compile without KCC and Animancer); its tests run from a game.

## Architecture

```text
input (game) ──► CharacterMotor ──► CharacterMotion ──► CharacterAnimator (Animancer)
                 (KCC motor:                              │
                  walk, sprint, jump,                     ▼
                  swim, follow)                    CharacterRig (LateUpdate)
                      ▲                             FootPlacement, HeadLook, TurnLean,
                      │ Suspend / Follow            MountLimbs (seat holds, item grips)
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
| `Rubickanov.Characters.Netcode` | `Rubickanov.Characters.Runtime`, `Rubickanov.Rig.Runtime`, `Rubickanov.Log.Runtime`, `KCC`, `Unity.Netcode.Runtime`, `Unity.Collections` | Optional, only with NGO: `NetworkCharacter`, snapshots, playback, parts and sides |
| `Rubickanov.Characters.Editor` | `Rubickanov.Characters.Runtime`, `Rubickanov.Rig.Runtime`, `Kybernetik.Animancer` | Editor only: `GaspImport` (Game Animation Sample clips as humanoids) |
| `Rubickanov.Rig.Tests` | `Rubickanov.Rig.Runtime`, NUnit | EditMode tests of the Rig math |
| `Rubickanov.Characters.Netcode.Tests` | the module, NGO, NUnit | EditMode: snapshot wire round trip, playback over a poor network |

## Layout

One namespace per assembly (`Rubickanov.Characters`, `Rubickanov.Rig`, `Rubickanov.Characters.Netcode`, `Rubickanov.Characters.Editor`); folders only
group files.

```text
Runtime/
├── Rig/                    math, no components
└── Characters/
    ├── CharacterProportions.cs
    ├── Motor/              CharacterMotor, input, motion, MovementConfig
    ├── Animation/          CharacterAnimator, CharacterAnimationsConfig, HandShape
    │   └── IK/             CharacterRig + config, FootPlacement, HeadLook, TurnLean, GetUpContacts, MountLimbs
    ├── Ragdoll/            CharacterRagdoll, Ragdoll, RagdollBone, RagdollConfig, GetUpSpot
    ├── Climb/              CharacterClimb, IClimbable, ClimbPoint
    └── Hooks/              what the game implements: input source, mount, water, foot ground, footprints, solid
└── Netcode/                optional module (NGO): NetworkCharacter, CharacterNetSnapshot, part writer/reader,
                            RemoteCharacterMotion, RagdollSnapshot, RemoteRagdoll, ICharacterNetPart/Frame,
                            ICharacterSpace, INetworkCharacterSide
Editor/Gasp/                GaspImport: GASP clips (UEFN Mannequin FBX) as humanoids on the game's model
Tests/Rig/
Tests/Netcode/
```

## Core Concepts

Every tunable lives in a config asset the game owns: **MovementConfig** (speeds, jump, swimming), **CharacterRigConfig**
(feet, head look, lean, seat grips, hand shapes), **CharacterAnimationsConfig** (clips), **RagdollConfig** (muscles,
falling, get-up, water), **CharacterProportions** (build data for a game's character builder).

A feature is off until the game turns it on: swimming needs a `Water`, footprints an `IFootprints`. Five config fields are where games chose differently; their defaults are the conservative side:

| Field | Default | Other value |
|---|---|---|
| `MovementConfig.FirstPersonTurn` | `Eased`: in first person the body eases round to the camera at `OrientationSharpness` | `AtOnce`: the body faces the camera at once (the game sets `CharacterInput.FirstPerson`) |
| `RagdollConfig.FallTime` | `0`: a long fall stays upright until it lands | `1`: after 1 s of falling the body goes down into its ragdoll in the air |
| `RagdollConfig.SafeSpot` | `false`: the capsule stands on the ground under the hips | `true`: `GetUpSpot` looks round that point in rings (`SpotStep` × `SpotRings` × 8) for stable ground, out of walls, with room to stand and nothing fixed over the lying head; with none the body stays down, looks again every `SpotRetry` and gets up where it lies after `SpotGiveUp` from the fall |
| `RagdollConfig.SolverVelocityIterations` | `1` (the physics default) | e.g. `4`: stiffer bones on a moving deck |
| `CharacterRigConfig.MountGrip` | `Wrist`: the wrist goes onto the hold, the hand keeps the clip's turn | `Palm`: the hand takes the hold's turn exactly (palm faces Z, fingers along Y), fingers shaped Fist/OpenPalm |

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
| `Carrier` | `CharacterRagdoll.Carrier` | Velocity of what the lying body rests on (a moving deck) |
| `SpeedMultiplier`, `JumpScale`, `GravityOverride` | `CharacterMotor` | Per-game scaling of the walk, jump and gravity |

```csharp
// Knocked down by a hit, or thrown off a vehicle with the vehicle's velocity.
ragdoll.Knockdown(impulse, hitPoint);
ragdoll.Throw(seatVelocity + Vector3.up * 2f);

// What the player asked for this step, before walls and slopes (e.g. to push a ball).
Vector3 wanted = motor.WantedVelocity;
```

## Netcode (optional)

`Runtime/Netcode` compiles only in a project that has Netcode for GameObjects (`com.unity.netcode.gameobjects` 2.0 or
newer): its asmdef turns `versionDefines` on that package into `RUBICKANOV_CHARACTER_NETCODE` and requires it in
`defineConstraints`. `package.json` does not depend on NGO, so a single-player game simply does not compile the module.

`NetworkCharacter` (a `NetworkBehaviour` on the character's root, fields `Character`, `Animator`,
`InterpolationDelay`, `TeleportDistance`) replicates the body. The owner simulates and sends a snapshot every tick, and
on its own step whenever a jump, landing, knockdown, get-up, sitting down or standing up, going in or out of the water
or a part asks for it (unreliable RPC to everyone else). Other machines play the snapshots back `InterpolationDelay`
behind (`Remote`, a `RemoteCharacterMotion` that drives the animator) and fire the events when playback reaches them.
While the ragdoll is the body, its bones go too (`RagdollSnapshot`, 72 B per step, played by `RemoteRagdoll` on the same
clock). `Knockdown(impulse, point)` works on any machine: a non-owner sends it to the owner (reliable). Ownership of a
character is fixed: neither spawn order nor despawn handles an owner change.

What a snapshot carries is what the package simulates: 52 B written field by field — time (f64), position, rotation, aim,
jump/fall/get-up counters, and one flags byte (grounded, swimming, seated, faces-look, face-up, down) — then one byte with
the parts' length and the parts. Off any part: 53 B, plus ~11 B of RPC framing, 30 a second.

A game adds its own data as parts and its glue as sides, components anywhere on the character:

```csharp
// A part: its bytes ride in the same snapshot and play back at that snapshot's time.
public sealed class EmotePart : MonoBehaviour, ICharacterNetPart
{
    public byte PartId => 2;                       // unique on this character
    public bool SendNow => _emote != _sent;        // asked every owner step: send this step at once
    public void Write(ref CharacterNetPartWriter w) { _sent = _emote; if (_emote != 0) w.WriteByte(_emote); }
    public void Play(CharacterNetPartReader r) => Show(r.ReadByte());   // replica, in order
    // ...
}
```

A frame part (`ICharacterNetFrame`, at most one) sends the pose in a moving space: `ToFrame` turns the owner's pose into
it at send, `Space(reader)` gives the replica its own copy of that space (`ICharacterSpace`: `ToWorld`, `PointVelocity`),
or null for the world. beach's deck is one: the vehicle's NetworkObjectId (1–2 B) and the pose in the deck's space.
`NetworkCharacter.Shown` fires on a replica after every playback update (before its first snapshot too), for a part that
places the shown body, and `NetworkCharacter.Ragdoll` gives the ragdoll found at spawn.

A side (`INetworkCharacterSide`) is the game's glue — camera, input, spawn point:

| Hook | When |
|---|---|
| `OnLocal` | this machine simulates the character (its player, the host's bots); before the package listens to it |
| `OnReplica` | another machine does; after the package made `Remote` and disabled the motor, before the components' `Start` |
| `OnLocalDespawn` | this machine's own copy goes away |
| `OnReplicaDespawn` | another machine's character goes away (must not touch this machine's camera or input) |

Both peers must run the same build of the module: NGO hashes RPCs by method name.

## GASP clips (editor)

`GaspImport` sets up clips of Epic's Game Animation Sample (FBX on the UEFN Mannequin skeleton, from the private
`rubickanov/gasp` export; Fab Standard License: usable outside Unreal, not for AI training) so they play on the game's
humanoid. The game keeps the FBX (with the GASP tree's paths and `RefPose.fbx`) and calls, from its own builder,
`GaspImport.Import(refPose, clipPaths, referenceAnimator, out reports)`:

- **Avatar.** `RefPose.fbx` becomes a humanoid with the UEFN Mannequin bone map (`HumanMap`); its legs, arms and fingers
  are turned so each bone points to the next as on the reference — a model of the game with a humanoid `Animator` in
  its T-pose — and twist shares are 1. A reference with full hands (middle and little fingers) also lays the palms; a
  mitten hand keeps GASP's hand-to-forearm turn. Both figures must face +Z in their root frames (the report prints it).
- **Root by kind** (`KindOf`, by name; no motion node): `*_flail_*` loop with all root motion in the pose; `*_getup_*`
  keep turn and height in the pose and give their level travel as root motion from the centre of mass (the capsule
  carries it); `*_poses_*` all in the pose; any other clip gives travel and turn as root motion.
- **End facing.** A get-up's body may end turned off the root's forward (GASP's stand ends at ~6°, crouch at 17–22°).
  The import measures the yaw of `Animator.bodyRotation` at the last frame on the reference, writes it as the clip's
  `rotationOffset` (Unity turns the clip by minus the offset), reimports and measures again; every get-up then ends
  facing root forward (residual 0°), so the capsule keeps one heading from start to end.
- **Report.** Per clip: root travel (reference metres and source metres, to check against GASP's `manifest.json`),
  the end yaw before and after, the hips at the first frame.

More clips: add the FBX under the game's GASP folder (same path as in GASP), list them where the game's config wants
them, and rebuild; the import settings are rewritten in the `.meta` files each time.

## Integration

What stays in each game: reading its own input and camera, the glue and parts of its network character, interaction
and items, the builders that make the character prefab and config assets, and the assets themselves (clips, configs,
prefabs). Script GUIDs in this package are the ones the games had, so prefabs keep their references.

Working on the package from a game: `./link.sh <game> local com.rubickanov.character` (absolute `file:` path and
`testables`), fix and check in the game, commit and push here, then `./link.sh <game> pin <sha>` in each game.

## Design Decisions

- **One copy for several games.** The games drifted apart while each kept its own copy; a fix is now made once.
- **Off unless the game asks.** A feature one game uses (swimming, the in-air ragdoll, palm grips, work hands) sits in
  the package but changes nothing in a game that sets no hook and keeps the config defaults.
- **No Asset Store code.** KCC and Animancer are referenced by assembly name only.
- **A hand goes where it is put.** `MountLimbs.GripHand` and `HoldHand` give the hand bone the grip's turn exactly and
  solve the arm for the wrist that follows, as two-bone IK end effectors do; the elbow goes by its hint. Nothing is
  clamped behind the author's back: `MountWristBend`/`MountWristTwist` and `AskedBend`/`AskedTwist` are for warnings.
