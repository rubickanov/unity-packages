# Kinematics

Deterministic pose maths on `double`: limb IK, foot contact, head look, reach and springs that give the same bits under Mono, IL2CPP and Burst on every platform.

## Dependencies

- `com.unity.mathematics` — `double3`, `double4`, `quaternion`
- `com.unity.burst` — tests only: the determinism test runs the maths in a Burst job

## Architecture

```
DetMath                  — sin, cos, atan2, asin, acos, exp, angles from +, −, ×, ÷ and sqrt only
└── Rotation             — quaternions as double4 (x, y, z, w)
    ├── TwoBoneIK        — arm or leg: bend → onto the target → toward the hint; the end keeps its rotation
    ├── ReachTarget      — a hand to a target with a speed cap, a stretch limit and a blend in and out
    ├── SoleContact      — a foot on a slope or a step edge, over an IGroundProbe
    ├── LimbPin          — a planted foot holds its contact, steps when it slips too far
    ├── PelvisTilt       — the pelvis follows the ground under both feet
    ├── HeadLook         — lower and upper neck turned toward a target within ranges
    └── TurnOffset       — lean into a turn
SpringStep / ScalarSpring / VectorSpring — critically damped springs, angle-aware
IGroundProbe / IGroundBatch / FlatGround  — the ground under a point, given by the game
```

Everything is a struct or a static method without managed references, so it runs inside a Burst job in `FloatMode.Deterministic`. The package knows nothing about bodies, worlds or Unity objects.

## Core Concepts

**Determinism** — `math.sin`, `math.exp`, `Mathf.*` and `float` round differently on Mono, IL2CPP and Burst; only IEEE `+ − × ÷ sqrt` are exact everywhere. `DetMath` builds the rest from them, so a pose hash recorded on one machine matches on another.

**Axes and units** — X right, Y up, Z forward. Angles are radians unless the name or type says degrees (`AimAngles`, `LookRange`, `PelvisTilt`, `TurnOffset`, `SolePlacement`).

**Ground probe** — `IGroundProbe` is implemented by the game (a raycast into its physics world) and passed by type (`where T : struct, IGroundProbe`), never boxed. `IGroundBatch` answers many points in one call with the same answers as one by one.

## Quick Start

Add the package by git URL; reference `Rubickanov.Kinematics` from the assembly that poses the body.

```csharp
using Rubickanov.Kinematics;
using Unity.Mathematics;

double4 bend = Rotation.AxisAngle(new double3(1, 0, 0), DetMath.Pi * 0.25);
double3 forward = Rotation.Rotate(bend, new double3(0, 0, 1));
```

## Usage

### Bending a leg onto the ground

```csharp
var leg = new TwoBoneChain(hipPosition, kneePosition, anklePosition, hipRotation, kneeRotation, ankleRotation);
double3 kneeHint = kneePosition + bodyForward;
TwoBoneChain solved = TwoBoneIK.Solve(leg, footTarget, kneeHint, weight: 1.0);
// solved.UpperRotation, solved.LowerRotation go to the hip and knee; the ankle keeps its world rotation
```

### Reaching a hand toward a target

```csharp
private ReachState _reach;

double3 hand = ReachTarget.Step(ref _reach, ReachSettings.Default, clipHand, shoulder, armLength,
    grabPoint, reaching: wantsToGrab, deltaTime: 1.0 / 120.0);
TwoBoneChain arm = TwoBoneIK.Solve(armChain, hand, elbowHint, _reach.Weight);
```

`ReachState` is plain data: keep it in the snapshot for rollback.

### Smoothing a value with a spring

```csharp
private ScalarSpring _lean;

double lean = _lean.Step(targetLean, new SpringSettings(frequency: 4.0, damping: 1.0), deltaTime);
double heading = _heading.StepAngle(targetHeading, settings, deltaTime); // wraps through ±π
```

### A flat floor probe

```csharp
var floor = new FlatGround { Height = 0.0 };
bool found = floor.Ground(anklePoint, new double3(0, 1, 0), above: 0.5, below: 0.5, out GroundSample ground);
```

## Design Decisions

- **`double` and `DetMath` only, no float variant** — a define switching precision would split the bits between games; a game on `float3` converts at its boundary.
- **Any formula change moves the golden hash** — `Tests/DeterminismTests` runs the cases under Burst and Mono, compares the bits and checks `GoldenHash`; re-record it on purpose and say why in the commit.
