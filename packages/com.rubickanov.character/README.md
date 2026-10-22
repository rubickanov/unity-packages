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
                      ▲                             FootPlacement, HeadLook, TurnLean, SwimPose,
                      │ Suspend / Follow            MountLimbs (seat holds, item grips)
                 CharacterRagdoll ──► Ragdoll (bones, pins, push) + GetUpContacts
                 CharacterClimb (IClimbable: ClimbPoint, game edges)
```

The motor owns the capsule. A fall suspends it: the ragdoll takes over, the capsule follows the hips upright, and the
get-up hands the body back.

## Assemblies

| Assembly | References | Contents |
|---|---|---|
| `Rubickanov.Rig.Runtime` | UnityEngine only | Math: `TwoBoneIK`, `Spring`, `AimAngles`, `TurnOffset`, `LimbPin`, `SoleContact`, `RagdollMath`, `Inertia`, `GetUpSearch` |
| `Rubickanov.Characters.Runtime` | `Rubickanov.Rig.Runtime`, `KCC`, `Kybernetik.Animancer`, `Rubickanov.Log.Runtime` | Motor, animator, ragdoll, rig, climbing, configs, hooks |
| `Rubickanov.Characters.Netcode` | `Rubickanov.Characters.Runtime`, `Rubickanov.Rig.Runtime`, `Rubickanov.Log.Runtime`, `KCC`, `Unity.Netcode.Runtime`, `Unity.Collections` | Optional, only with NGO: `NetworkCharacter`, snapshots, playback, parts and sides |
| `Rubickanov.Characters.Editor` | `Rubickanov.Characters.Runtime`, `Rubickanov.Rig.Runtime`, `Kybernetik.Animancer` | Editor only: `GaspImport` (Game Animation Sample clips as humanoids), `GetUpBaker` (nearest-frame get-up data) |
| `Rubickanov.Rig.Tests` | `Rubickanov.Rig.Runtime`, NUnit | EditMode tests of the Rig math (inertialization, get-up search) |
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
    ├── Animation/          CharacterAnimator, CharacterAnimationsConfig, HandShape, GetUpPoses, PoseInertia, StrideMarkers
    │   └── IK/             CharacterRig + config, FootPlacement, HeadLook, TurnLean, SwimPose, GetUpContacts, MountLimbs
    ├── Ragdoll/            CharacterRagdoll (+ .GetUp: nearest-frame get-up), Ragdoll, RagdollBone, RagdollConfig,
    │                       GetUpSpot, FlailReflex, RagdollTouch, ShellContact (+ ShellHit), CharacterGetUp
    ├── Climb/              CharacterClimb, IClimbable, ClimbPoint
    └── Hooks/              what the game implements: input source, mount, water, foot ground, footprints, solid
└── Netcode/                optional module (NGO): NetworkCharacter, CharacterNetSnapshot, part writer/reader,
                            RemoteCharacterMotion, RagdollSnapshot, RemoteRagdoll, ICharacterNetPart/Frame,
                            RagdollState, IRagdollNetState, IRagdollNetFrame, ICharacterSpace, INetworkCharacterSide
Editor/Gasp/                GaspImport: GASP clips (UEFN Mannequin FBX) as humanoids on the game's model
Editor/GetUp/               GetUpBaker: every frame of the config's get-ups on the game's character → GetUpPoses
Editor/Strides/             StrideBaker: every frame of the config's locomotion loops on the game's character → StrideMarkers
Tests/Rig/
Tests/Netcode/
```

## Core Concepts

Every tunable lives in a config asset the game owns: **MovementConfig** (speeds, jump, swimming), **CharacterRigConfig**
(feet, head look, lean, swimming pose, seat grips, hand shapes), **CharacterAnimationsConfig** (clips), **RagdollConfig** (muscles,
falling, get-up, water), **CharacterProportions** (build data for a game's character builder).

A feature is off until the game turns it on: swimming needs a `Water`, footprints an `IFootprints`. Nine config fields are where games chose differently; their defaults are the conservative side:

| Field | Default | Other value |
|---|---|---|
| `MovementConfig.FirstPersonTurn` | `Eased`: in first person the body eases round to the camera at `OrientationSharpness` | `AtOnce`: the body faces the camera at once (the game sets `CharacterInput.FirstPerson`) |
| `RagdollConfig.FallTime` | `0`: a long fall stays upright until it lands | `1`: after 1 s of falling the body goes down into its ragdoll in the air |
| `RagdollConfig.SafeSpot` | `false`: the capsule stands on the ground under the hips | `true`: `GetUpSpot` looks round that point in rings (`SpotStep` × `SpotRings` × 8) for stable ground, out of walls, with room to stand and nothing fixed over the lying head; with none the body stays down, looks again every `SpotRetry` and gets up where it lies after `SpotGiveUp` from the fall |
| `RagdollConfig.Flail` | `false`: in the air the body holds the pose it fell in and fades to limp over `LimpTime` | `true` (with three `CharacterAnimationsConfig.Flails` clips: head first, along the fall, feet first): `FlailReflex` drives the joints toward the flail clip's pose (`FlailStrength` × peak torque, `FlailHz`), the clip chosen by the pelvis's up every `FlailChooseTime`; a touch over `FlailTouchForce` eases them to limp over `FlailEaseTime` (arms `FlailArmsTime`); water lets go at once. Bones get a `RagdollTouch` only then |
| `CharacterAnimationsConfig.GetUps` (+ `GetUpPoses`) | empty: the body gets up by `GetUpFaceUp` / `GetUpFaceDown`, the clip held on the ragdoll's pose and blended in over `GetUpBlendTime` | a list of get-up clips and their baked `GetUpPoses`: the get-up starts from the frame, clip and heading nearest the lying body (see Nearest-frame get-up) |
| `CharacterAnimationsConfig.InertialGetUp` (and `InertialJump`, `InertialLand`, `InertialTurn`, `InertialWaterEntry`) | `false`: clips cross-fade | `true` (with a `PoseInertia` on the model, `CharacterAnimator.Inertia`): the new clip plays at once and the offset to the old pose dies away over `GetUpInertia` / `SwitchInertia` s, keeping its velocity |
| `RagdollConfig.ShellLayer` | `-1`: no shell; an upright body has no bones on, so nothing body-shaped for a flying ragdoll to hit | a layer (the game makes it meet only its ragdoll layer): `CharacterRagdoll.Shell = true` turns an upright body's bones on as a shell (see Shell) |
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
    JumpHeld = jumpHeld,
});

motor.InputSource = playerInput;   // or: an ICharacterInputSource read in the motor's Update
```

Air control approaches the walking or sprinting speed, capped by `MaxAirMoveSpeed`, at
`AirAccelerationSpeed` m/s². `AirDecelerationSpeed` brakes when slowing, releasing movement or reversing;
external momentum is reduced gradually, never clamped to the control limit. `SpeedMultiplier` applies in
the air too. `Drag` affects horizontal velocity only.

`FallGravityMultiplier` makes descent faster. `JumpReleaseGravityMultiplier` shortens ascent after
releasing `JumpHeld`; pressing again cannot restore that jump. Both default to 1 (ordinary gravity).
Only a ground/coyote jump is shortened: upward impulses and swimming use their own motion.
`JumpPressed` is still a latched press edge with the pre/post-grounding grace windows; holding the button
does not jump again on landing. An already released buffered press produces a short jump.

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
| `IRagdollWater` | `CharacterRagdoll.WaterHook` | Each step a downed body is simulated: `Float` as configured, or the game takes the water over (`Pushed` wet, `Carried` dry), e.g. a body skipping over the sea; `BodyGravity` is what pulls it, `Ragdoll.Touch` how hard it presses on anything else (flail on) |
| `Carrier` | `CharacterRagdoll.Carrier` | Velocity of what the lying body rests on (a moving deck) |
| `SpeedMultiplier`, `JumpScale`, `GravityOverride` | `CharacterMotor` | Per-game scaling of the walk, jump and gravity |
| `RigSway` | `CharacterRig.Sway` | An extra lean the game lays on the pose each frame (e.g. a drunk's balance): `Lean` tips hips, spine and chest about the feet by the `LeanBones` shares (world, degrees; the pelvis shifts and sinks so both feet still reach), `HeadLean` on the head after the look, `ArmHang` turns the upper arms back so they hang plumb, `Stance` sets the grounded feet wider; default (all zero) is the rig as before, ignored on a seat |
| `RigFoot` | `CharacterRig.LeftFoot`, `RightFoot` | A foot the game places in the world each frame (e.g. a drunk's catch step): `Point` is where the ankle goes as an animated ankle would (its height over the base is the lift; the ground under it is probed as for an animated foot), `Weight` blends from the animated foot; held still it stays planted while the body moves, and the pelvis sinks so a foot set wide still reaches. `AnimatedFoot`, `Ankle`, `Knee` and `LegLength` read the legs back; default (weight 0) leaves the feet to the animation exactly as before; ignored on a seat. |
| `StayDown`, `Limp` | `CharacterRagdoll` | A body down does not get up by itself (a `GetUp` waits) / lies limp, no flail and no muscles; both off by default, set them on every machine (only the simulating one acts), e.g. asleep or dead |

```csharp
// Knocked down by a hit, or thrown off a vehicle with the vehicle's velocity.
ragdoll.Knockdown(impulse, hitPoint);
ragdoll.Throw(seatVelocity + Vector3.up * 2f);

// What the player asked for this step, before walls and slopes (e.g. to push a ball).
Vector3 wanted = motor.WantedVelocity;
```

## Shell

An upright character's capsule is its body; its ragdoll bones are off, so a flying ragdoll passes through it. With
`RagdollConfig.ShellLayer` set, `CharacterRagdoll.Shell = true` asks for a shell (`RagdollMode.Shell`): the bones on,
kinematic, interpolation off, every bone collider on the shell layer (its own layer given back after), moved each
physics step by `MovePosition` / `MoveRotation` onto the animated pose as drawn last frame (after animation and IK),
put on the root as it is at the end of the step: the KCC's transient pose, the follow target when suspended (a seat),
the drawn root on a replica. Moved, not placed, so contacts carry the body's real velocity; a pose jump
(`Ragdoll.ClearSamples`) or a step over 2 m places it instead. The shell is a want: it starts on an upright body whose
bones are otherwise off and that shows no stream, on any machine (a replica reads its drawn pose too), ends when
unwanted, and comes back after a get-up while still wanted. `Shelled` says whether it is on now. Asked for where it
can start, it starts at once (its bones placed on the pose), so a caller in the physics step can, e.g., let bones
already inside it pass through before the step is simulated.

- `ShellHit` (bone, other collider, first contact point, normal out of the other body into the bone, relative velocity,
  impulse) is raised by `CharacterRagdoll.ShellHit` from a `ShellContact` that the bones carry only while shelled.
- A fall (`Knockdown`, `Throw`, `TakeOver`, a replica's `ShowDown` / stream) goes on from the shell's bones where they are
  (no rest-pose detour, no teleport), layers back, with the pose's velocity plus the capsule's as from an upright body;
  if the pose was moved away from the shell meanwhile (over 0.3 m, e.g. the root put on a seat), from the pose as before.
- With a shell layer set, `CharacterRagdoll` adds it to `CharacterRig.IgnoredGroundLayers`: the foot and get-up
  rays never take shell bones (its own around the legs, a neighbour's) for ground. Without it the feet climb onto the
  own shins and the legs fold up step by step.
- `Ragdoll.ShowsBody` (active or kinematic) is "the bones are the body"; a shell is not. `Ragdoll.ShellOffset()` is the
  farthest bone from the drawn pose, m. Profiler marker `Ragdoll.Shell`.

```csharp
ragdoll.Shell = nearFastRagdoll;          // the game decides who; the package keeps the shell on the pose
ragdoll.ShellHit += hit => Decide(hit);   // hit.Other is the flyer's bone collider
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
clock, or a ragdoll frame's, below). `Knockdown(impulse, point)` works on any machine: a non-owner sends it to the owner (reliable). `Knockdown(impulse, point, striker)` names the striker's `NetworkObjectId`: the machine that simulates the fall raises `KnockedBy(striker)` there, so a game can let the fall pass through the striker's replica, which shows the hit up to a round trip later. Ownership of a
character is fixed: neither spawn order nor despawn handles an owner change.

What a snapshot carries is what the package simulates: 55 B written field by field — time (f64), position, rotation, aim,
jump/fall/get-up counters, one flags byte (grounded, swimming, seated, faces-look, face-up, down), the get-up's clip (a
byte, 255 = face up / face down) and start time (ushort ms) — then one byte with the parts' length and the parts. Off
any part: 56 B, plus ~11 B of RPC framing, 30 a second. A replica gets up by the same clip from the same time: the
capsule is held at the last lying snapshot and set where the owner stood it as the get-up plays, and the pose shown
until then is where its inertia starts. `CharacterRagdoll.GettingUp` is an `Action<CharacterGetUp>` (`FaceUp`, `Clip`,
`Time`).

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
A ragdoll frame (`IRagdollNetFrame`, at most one, any component) does the same for the bones, and lets a live body be
streamed while upright: `Streams` (owner, each upright step) sends the pose then too; `FrameOf` gives the frame id (0 =
the root's space on the owner's clock, as without it), its pose on this machine and the time on its clock, and the hips
go in that frame (`Ragdoll.CaptureIn`, from the bodies: an interpolated bone's transform lags in the physics step);
`Resolve(frame)` gives the replica its copy of the space and the time it is shown at, and `RemoteRagdoll` plays the
frame's poses on that clock. An upright replica shows such a stream on kinematic bones instead of its animation
(`CharacterRagdoll.ShowStreamed`, placed in its LateUpdate after the root moved; `ShowsStreamed`), back to the animation
`RemoteRagdoll.StreamTimeout` (0.25 s) after the last pose (`EndStreamed`); going down it carries on without turning
the bones off. beach's ride is one: a rider live on a towed ride in the ride's space on the server's clock, then the
world on the same clock after the throw. `RagdollSnapshot.Frame` (u32) keeps the snapshot at 72 B.
A ragdoll can be lent: the server's `Lend(client)` makes another machine simulate it (`RagdollSimulator`, a
host-written NetworkVariable with a hand-over count `Lends`; the owner by default, `Lend(OwnerClientId)` gives it back),
e.g. a rider's body on the machine that drives the ride, so it rides the same physics step as the ride. Ownership of the
NetworkObject does not change. `SimulatesRagdoll` is true on the simulator, `RagdollLent` while it is not the owner;
`CharacterRagdoll.IsReplica` follows "simulated elsewhere" (a body down when it becomes a replica holds still for the
stream). At each hand-over the old simulator captures the bodies (`Ragdoll.CaptureState`: every bone's position,
rotation, velocity and spin, in the ragdoll frame) and sends a reliable `RagdollState` (~500 B, plus up to 64 B of the
game's own `IRagdollNetState`) to the new one, which seeds from it (`Ragdoll.Seed`, `CharacterRagdoll.TakeOver`); if
none comes within `StateWait` (0.5 s) it goes on from the pose it shows. A non-owner simulator streams the ragdoll in
the ragdoll frame only (never frame 0), at the tick rate; poses go `SendTo.NotMe` and anything not from the current
simulator is dropped. A fall on the simulator reaches the owner (its fall counter, the down state) and everyone else; a
knockdown goes to the simulator. While lent, the owner shows the poses like any replica and its capsule follows the
shown hips. A lent body that comes to rest down does not get up there (`CharacterRagdoll.GetsUpElsewhere`,
`WouldGetUp`): the simulator hands it back with `GetUp` set, and the owner gets up from the pose it shows, so the body
does not jump. `LentDown` (server) tells whether the lent body is down on its simulator.
A simulator can give a body up at once with `HandUp()`, e.g. when the vehicle it rides is handed to another machine: it
captures the state at that instant, parks it with the server and shows the body from its own last pose until the next
simulator takes it; the server's `Lend(client, anew)` forwards the parked state (`anew` lends again to the machine that
handed it up). The game's `IRagdollNetState` can make a taker wait for its own world (`CanTake`, at most `StateWait`,
e.g. until the ride is simulated there) and go on itself from an upright live body's bones (`TakeUpright`). When the
last simulator is gone without a state, an upright body shown from a stream goes on from the bones as they are drawn
(`TakeUpright` with the world pose and no game bytes).
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

## Nearest-frame get-up

With `CharacterAnimationsConfig.GetUps` set, a body that gets up on land (`GetUpSearch`, a port of Sinew's
`MotionSearch.Nearest`) compares the lying ragdoll — joint turns of the 11 ragdoll joints, the pelvis against gravity
(×4, any heading) and which of the 12 segments bear on the ground (heights capped at 0.25 m) — with every baked frame
of every clip up to its stand frame minus 0.5 s, and takes each clip's nearest frame and heading. Cheapest first, a
candidate must have a safe spot at its root (`GetUpSpot`, when `SafeSpot`), not lead the trunk or head into a fixed piece
overhead, and not sweep its body through another character that is down, getting up or standing; if none passes the
body waits, looks again every `SpotRetry` and gets up anyway after `SpotGiveUp`. The capsule goes to the match's root and
heading, the clip plays from the found time (with `InertialGetUp`, at once: the ragdoll's pose is the offset that dies
away; without, the clip waits on the ragdoll's pose and blends in), and control comes back at the clip's stand frame
(the first frame at 95 % of the final pelvis height that never sinks below 90 % after), blending to locomotion over
`GetUpBlendOut`. Bodies in deep water, a stale bake and a config without clips take the face-up / face-down path; a
stale bake warns once.

The data is baked, not computed at runtime: `GetUpBaker.Bake(characterPrefab, config.GetUps, poses)` samples every
frame of each listed clip on the game's character (joint turns, pelvis, segment heights, a whole-body path every
0.1 s) into the `GetUpPoses` asset the config points at. **More get-up clips = add the FBX, add them to `GetUps`, and
rebuild the poses** from the game's builder (beach: Game/Builders/Build GetUp Poses). Entries are checked against the list on
first use (names, lengths, frame counts).

`PoseInertia` (on the model, `[DefaultExecutionOrder(-60)]`: after Animancer, before `CharacterRig`) lays an
inertialization offset (`Inertia`, a port of Sinew's `Inertialization`: the offset falls as
`(1 − s)⁴(x₀ + (v₀T + 4x₀)s)`, keeps its velocity, never overshoots) over ~22 humanoid bones and the hips' place. It
records the shown pose every frame only while a switch kind other than the get-up is on, so it costs nothing otherwise.

## Stride sync

Animancer stops a state whose fade-out has ended, and stopping rewinds it, so after a landing, a jump, a turn or a
get-up the locomotion mixer comes back from its first frame — one foot's stance, whichever foot the shown pose bears
on. With `CharacterAnimationsConfig.StrideSync` on (off by default), `CharacterAnimator` tracks the shown pose's
reach (left foot minus right along the model's forward) every frame, and when the mixer restarts it takes the clip
weighing most in the blend and sets the mixer's normalized time to that clip's frame in the stance of the foot that
bears (the foot moving back against the other: a falling reach is the left's stance) with the nearest reach. The
mixer keeps its children in step by normalized time, so one time sets them all. A body that comes back standing (the
idle has no strides) starts from the start as before.

The strides are baked, not computed at runtime: `StrideBaker.Bake(characterPrefab, StrideBaker.LocomotionClips(config),
markers)` samples every frame of the clips of the Locomotion, Uphill and Downhill mixers on the game's character into
the `StrideMarkers` asset the config points at: per frame the reach, and each foot's touch-down markers (`Strides`, a
port of Sinew's `ClipMarkers.FindMarkers`: the foot farthest ahead, peaks 0.2 s apart, at least 0.34 of the leg
ahead). Strafes whose feet never pass each other that far have no strides and start from the start. **More locomotion
clips = add them to the mixers and rebuild the markers** (beach: Game/Builders/Build Stride Markers). A clip baked
under its name but with another length or frame count warns once and starts from the start.

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
