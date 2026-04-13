# CharacterMotor v2 - KCC-Inspired Plan

This document captures the architectural direction for evolving `com.rubickanov.character-motor` after analyzing Philippe St-Amand's Kinematic Character Controller (KCC).

Goal: reach KCC-grade movement robustness while preserving the package's strongest advantages:

- pure C# simulation mindset
- modular high-level movement behaviors
- explicit snapshots for prediction/reconciliation
- clean package structure
- testability

This is not a plan to clone KCC 1:1.
This is a plan to absorb its strongest low-level ideas while keeping the parts of the current architecture that are better aligned with the wider Rubickanov package ecosystem.

## Executive Summary

Decision:

- keep modular architecture at the gameplay behavior layer
- stop forcing deep low-level collision/grounding logic to stay modular
- introduce a more cohesive KCC-style solver core under the existing module layer

Target model:

- low-level solver: cohesive, stateful, geometry-heavy, authoritative
- high-level modules: compositional, readable, gameplay-facing

In short:

- `collision/grounding/steps/ledges/movers` -> more KCC-like
- `movement/jump/crouch/sprint/states` -> keep current modular approach

## Why Not Fully Switch To KCC's Architecture

KCC is extremely strong as a movement core, but its extension model is centered on:

- one large motor
- hook-based callbacks
- large user-facing controller classes

This is good for shipping a robust controller fast, but it is weaker for this ecosystem's goals:

- harder to compose behavior cleanly
- easier to accumulate "god controller" classes
- less aligned with the package ecosystem's design language
- more Unity-centric than desired

Switching fully to KCC's style would likely improve low-level movement quality, but at the cost of the package's long-term architectural value.

## Why Not Keep The Current Design Unchanged

The current `CharacterMotor` has real strengths:

- `MotorSimulation`
- `IMotorModule`
- `IStatefulModule`
- snapshots for reconciliation
- a clear split between simulation and visual update

However, KCC makes one thing obvious: some problems are too tightly coupled to remain spread across small modules without losing robustness.

These low-level concerns should not remain fragmented:

- collision sweep solving
- depenetration / initial overlap safety
- grounding classification
- step validity
- ledge detection
- moving platform attachment semantics
- rigidbody interaction semantics

These are solver problems, not gameplay-feature modules.

## Direct Comparison

| Area | KCC | Current CharacterMotor | Winner |
|---|---|---|---|
| Low-level movement maturity | Very high | Good base, but simpler | KCC |
| Grounding model | Rich and explicit | Basic | KCC |
| Step handling | Deep validity checks | Simpler heuristic approach | KCC |
| Ledge handling | First-class | Minimal | KCC |
| Moving platforms | First-class with attachment semantics | Partial via ground velocity | KCC |
| Rigidbody interaction | Strong | Limited | KCC |
| Pure C# simulation mindset | Weak | Strong | CharacterMotor |
| Modular behavior composition | Weak | Strong | CharacterMotor |
| Prediction/reconciliation fit | Possible, but not core-oriented | Strong | CharacterMotor |
| Package ecosystem fit | Weak | Strong | CharacterMotor |
| Walkthrough/examples | Excellent | Limited | KCC |

Conclusion:

- KCC is the benchmark for solver maturity
- CharacterMotor is the better long-term ecosystem asset

Therefore:

- do not replace CharacterMotor with KCC
- do not turn CharacterMotor into a KCC clone
- do rebuild the low-level solver using lessons from KCC

## Target Architecture

### Layer 1 - Solver Core

Introduce a cohesive low-level solver layer responsible for:

- sweep movement
- overlap handling / depenetration
- grounding
- step detection
- ledge detection
- moving platform support
- rigidbody interaction rules

This layer should own transient geometric truth.

Possible names:

- `MotorSolver`
- `KinematicMotorSolver`
- `CharacterMotorSolver`

The exact type name matters less than the architectural role.

This layer should be:

- pure C# where possible
- deterministic-friendly
- explicit about transient state and reports
- the single source of truth for contact and grounding evaluation

### Layer 2 - Reports And State

Introduce richer reports and state structures.

Required additions:

- `GroundingReport`
- `TransientGroundingReport`
- `HitStabilityReport`
- `MoverAttachmentState`
- richer body snapshot data

These reports should replace the current overly thin grounding model.

Current state is too small:

- `IsGrounded`
- `GroundNormal`
- `GroundAngle`
- `GroundVelocity`

Target state should distinguish:

- any ground found vs stable ground
- inner vs outer ground normals
- snapping allowed vs prevented
- ledge context
- attached mover identity and velocity contribution

### Layer 3 - High-Level Behavior Modules

Keep modules, but move them up the stack.

Modules should no longer own geometric truth.
They should:

- read solver reports
- express gameplay intent
- modify desired velocity / flags / state
- participate in snapshotting where needed

Examples:

- `MovementModule`
- `JumpModule`
- `SprintModule`
- `CrouchModule`
- `SlopeSlideModule`

These remain valuable and should survive the redesign.

## Architectural Principle

The guiding principle for v2:

Low-level contact logic is centralized.
High-level movement behavior is modular.

That is the core compromise between current CharacterMotor and KCC.

## What To Keep From Current CharacterMotor

These are strategic strengths and should be preserved:

- `MotorSimulation` as the orchestration center
- `IMotorModule`
- `IStatefulModule`
- `MotorStateSnapshot` / `SaveState` / `RestoreState`
- explicit simulation vs visual update split
- package structure
- unit tests around modules and simulation behavior

The v2 effort should strengthen the motor without sacrificing these.

## What To Borrow From KCC

### 1. Grounding Model

This is the single most important area to absorb.

Add a richer grounding report with fields equivalent in spirit to:

- `FoundAnyGround`
- `IsStableOnGround`
- `SnappingPrevented`
- `GroundNormal`
- `InnerGroundNormal`
- `OuterGroundNormal`
- `GroundCollider`
- `GroundPoint`

This report should exist both in:

- current frame evaluated form
- transient/snapshot-friendly form

Why:

- stable gameplay decisions depend on more than a bool
- jump, slope, ledge, and snap behavior all benefit from richer grounding data
- many KCC robustness wins come from this richer model

### 2. Ledge Handling

Add real ledge evaluation to the solver.

Desired ledge data:

- `LedgeDetected`
- `IsOnEmptySideOfLedge`
- `DistanceFromLedge`
- `IsMovingTowardsEmptySideOfLedge`
- `LedgeGroundNormal`
- `LedgeRightDirection`
- `LedgeFacingDirection`

Why:

- ledges are one of the main places where "feels good" controllers separate from fragile ones
- ground snapping, stable ground detection, and air transition behavior all depend on this

### 3. Step Validity Instead Of Simple Step Detection

Current step climbing is useful but too heuristic.

Need KCC-inspired validity logic:

- detect potential step candidate
- test target position validity
- verify no overlaps at target position
- verify sufficient step depth
- verify stable surface
- support multiple stepping modes if needed

Likely config concepts:

- `StepHandlingMethod`
- `MaxStepHeight`
- `AllowSteppingWithoutStableGrounding`
- `MinRequiredStepDepth`

Whether names match KCC is less important than the behavior.

### 4. Moving Platform / Mover Attachment Model

Current `GroundVelocity` is not enough.

Need a stronger mover model:

- attached mover identity
- linear mover velocity contribution
- angular mover contribution
- attach/detach semantics
- preserve momentum on leaving mover

Likely pieces:

- `MotorMover` or `PhysicsMover` equivalent
- explicit `MoverAttachmentState`
- optional interpolation deltas for camera/view systems

### 5. Force Unground And Controlled Snap Behavior

Add explicit APIs for:

- force unground
- disable snap for a short time
- jump-specific ungrounding
- special movement state ungrounding

Why:

- jump quality and consistency improve dramatically
- avoids fighting the ground solver during intentional takeoff

### 6. Surface Tangent Utilities

Promote robust surface helpers into solver/core utilities:

- tangent-to-surface direction
- projected velocity on stable surface
- air-to-wall obstruction filtering

These should be reusable, tested primitives.

### 7. Initial Overlap Safety / Decollision Passes

KCC explicitly handles overlap cases before or during movement solving.

CharacterMotor should gain:

- optional initial overlap pass
- explicit depenetration iteration budget
- safer movement when already intersecting geometry

Why:

- robustness
- fewer tunnel / jam edge cases
- easier to trust in real levels

### 8. Better System-Level Simulation Ordering

KCC has an explicit simulation system that updates movers and motors in a defined order.

CharacterMotor should adopt similar clarity, while keeping current package conventions.

Need:

- deterministic update ordering between movers and characters
- pre-sim state capture
- post-sim interpolation hooks
- cleaner multi-character orchestration

## What Not To Copy

Do not copy these aspects of KCC directly:

- one giant `KinematicCharacterMotor.cs` as the center of all logic
- hook-based extension model as the only extension model
- huge user-facing controller classes
- asset-style folder organization
- no-asmdef / no-package structure
- example-state logic as part of core architecture

The goal is not to become KCC.
The goal is to become KCC-grade where it matters.

## Proposed CharacterMotor v2 Shape

Possible structure:

- `Runtime/Core/MotorSimulation.cs`
- `Runtime/Core/MotorSolver.cs`
- `Runtime/Core/Grounding/GroundingReport.cs`
- `Runtime/Core/Grounding/TransientGroundingReport.cs`
- `Runtime/Core/Collisions/HitStabilityReport.cs`
- `Runtime/Core/Movers/MotorMover.cs`
- `Runtime/Core/Movers/MoverAttachmentState.cs`
- `Runtime/Body/KinematicMotorBody.cs`
- `Runtime/Body/RigidbodyMotorBody.cs`
- `Runtime/Modules/*`

Conceptual data flow:

1. input and external state are gathered
2. behavior modules write intent and policy
3. solver evaluates collisions, grounding, steps, ledges, movers
4. body is updated
5. reports are finalized
6. snapshot emitted

## Suggested Refactor Direction By Existing Type

### `MotorSimulation`

Keep as the orchestration root.

Responsibilities:

- own state
- own module list
- own solver
- run simulation phases in order
- save and restore snapshots

Should gain:

- explicit solver phase boundaries
- cleaner mover ordering support
- richer snapshot coverage

### `KinematicMotorBody`

This is the main place where current implementation is too simple relative to KCC.

Should evolve to support:

- initial overlap safety
- richer sweep hit gathering
- depenetration iterations
- closer cooperation with solver reports
- mover-aware movement semantics

It should no longer carry all responsibility alone.
It should become a stronger low-level body primitive under the solver.

### `GroundDetectionModule`

Should be reduced from "owner of grounding truth" to "consumer/wrapper/policy reader".

Its current role is too fundamental.
Grounding should move into the solver.

Possible end state:

- removed entirely
- or converted into a policy/config surface

### `StepClimbModule`

Should no longer own geometric step discovery.

Instead:

- solver detects valid steps
- module decides if gameplay conditions allow using them

This will reduce brittle geometry logic in high-level modules.

### `MovementModule`

Keep.

This is a good module example because it expresses intent rather than geometry.

It should continue to:

- interpret input
- produce desired movement direction/velocity
- support orientation modes

### `PhysicsResolverModule`

Probably split conceptually.

The current module mixes:

- intent shaping
- air/ground acceleration rules
- gravity policy
- low-level body application assumptions

In v2:

- gameplay movement policy stays in a high-level module
- low-level physical solve belongs to the solver

### `JumpModule`, `SprintModule`, `CrouchModule`, `SlopeSlideModule`

Keep and adapt.

They remain good gameplay modules.
They simply need richer solver data.

## Migration Strategy

### Phase 1 - Design And State Expansion

Do first:

- define new reports and snapshot structures
- document solver responsibilities
- identify which current module responsibilities move into solver

No major behavior rewrite yet.

### Phase 2 - Introduce Solver Data Without Breaking Public API

Add:

- `GroundingReport`
- `HitStabilityReport`
- mover attachment state

Keep old fields temporarily, but derive them from richer data.

This minimizes breakage.

### Phase 3 - Rebuild Grounding

Implement:

- any-ground vs stable-ground distinction
- richer normals
- snap prevention state
- better slope classification

This phase unlocks better jumping, stepping, and ledges.

### Phase 4 - Rebuild Steps And Ledges

Implement:

- valid step detection
- ledge report generation
- better edge stability logic

Refactor `StepClimbModule` accordingly.

### Phase 5 - Add Mover System

Introduce:

- mover component/type
- attachment semantics
- velocity and rotation transfer
- detach momentum policy

This phase is critical for reaching KCC-like quality.

### Phase 6 - Refine High-Level Modules

Only after solver maturity improves:

- retune jump
- retune crouch
- retune slope slide
- retune air control

These should be adjusted against the new solver rather than patched blindly.

### Phase 7 - Add Stress Testing And Walkthroughs

KCC's strongest non-runtime asset is its knowledge transfer.

CharacterMotor should eventually gain:

- dedicated example scenes
- regression scenes for movement edge cases
- stress test scene
- documentation showing intended extension patterns

## Testing Strategy

KCC has many examples.
CharacterMotor should beat it in test discipline.

Add tests for:

- stable vs unstable ground classification
- ledge detection cases
- stepping validity rules
- mover attachment/detachment
- snapshot roundtrip with richer solver state
- jump unground correctness
- overlap recovery

Also add scene-level integration cases for:

- steps
- slopes
- ledges
- moving platforms
- crouch under ceiling
- jump near ledge
- high-speed downhill transitions

## Success Criteria

CharacterMotor v2 is successful if:

- movement feels as robust as KCC in real level geometry
- solver edge cases are significantly reduced
- modular gameplay behavior remains readable and composable
- prediction/reconciliation support stays first-class
- the package remains cleanly organized and testable

## Final Decision

Keep the package's modular architecture at the high level.
Adopt a more cohesive KCC-style low-level solver under it.

Do not:

- replace CharacterMotor with KCC
- clone KCC's architecture wholesale
- preserve current low-level modularity as dogma

Do:

- keep CharacterMotor as the long-term ecosystem asset
- use KCC as a benchmark for movement maturity
- absorb KCC's strongest solver ideas deliberately

That is the path most likely to produce a controller that is both:

- architecturally right for this ecosystem
- physically mature enough to compete with KCC
