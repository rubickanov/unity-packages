# ACS — Folder Structure

Split along the **Core (pure C#)** vs **Unity** axis. Everything in `Core/`
compiles without `UnityEngine` — the goal is that `Core/` can be lifted into
its own assembly (or a non-Unity project) for headless simulations,
dedicated-server hosts, and edit-mode tests that never boot the player loop.

## Runtime

```
Runtime/
├── Core/                              # pure C# — no UnityEngine
│   ├── Aspects/
│   │   ├── IEntityAspect.cs
│   │   ├── AspectAttribute.cs
│   │   └── AspectInjector.cs          # Expression-tree-based, reflection only
│   ├── Entities/
│   │   ├── IEntity.cs
│   │   ├── Entity.cs
│   │   └── EntityExtensions.cs        # AttachLogic(...)
│   ├── Behavior/
│   │   ├── IEntityLogic.cs
│   │   └── ITickable.cs
│   └── World/
│       ├── World.cs                   # pure; after Entity/MonoEntity-style split
│       ├── EntityRegistry.cs
│       └── EntityQuery.cs             # 8 arity overloads
├── Unity/                             # MonoBehaviour adapters
│   ├── Entities/
│   │   ├── MonoEntity.cs
│   │   └── SingletonMonoEntity.cs
│   ├── Behavior/
│   │   ├── IEntityComponent.cs        # marker for the Unity-tier component
│   │   ├── EntityComponent.cs
│   │   ├── EntityTickRunner.cs
│   │   └── EntityInjector.cs          # static DI hook (GameObject -> void)
│   └── World/
│       └── MonoWorld.cs               # future — see acs/IDEAS.md
├── ACS.Runtime.asmdef
├── AssemblyInfo.cs
└── csc.rsp
```

### What lives where

- **`Core/Aspects/`** — the data contract. `IEntityAspect` marker, `[Aspect]`
  attribute, and the reflection-driven injector. `AspectInjector` targets
  `IEntity` (not `MonoEntity`) so both tiers reuse the same path.
- **`Core/Entities/`** — pure `Entity`, the `IEntity` contract, and the
  `AttachLogic` helper for wiring `IEntityLogic` to an entity's `Destroyed`.
- **`Core/Behavior/`** — pure behavior tiers. `IEntityLogic` (reactive,
  auto-disposed) and `ITickable` (per-step). Neither touches Unity.
- **`Core/World/`** — world-scoped state + the registry + queries. Currently
  two files (`World` / `WorldCore`); collapse to a single pure `World` when the
  `MonoWorld` split lands (see IDEAS.md).
- **`Unity/Entities/`** — `MonoEntity` and the singleton base.
- **`Unity/Behavior/`** — Unity-tier component (`EntityComponent : MonoBehaviour`),
  its marker, the tick runner, and the DI delegate.
- **`Unity/World/`** — scene-anchored world (`MonoWorld`, future). Hosts
  world-scoped `EntityComponent`s on its GameObject; pure `World` lives in
  `Core/World/` and is what `MonoWorld` delegates to.

### Extractability check

Every file under `Core/` must not reference `UnityEngine.*`. CI would enforce
this; for now it is a review rule. If a Core file needs a Vector3-like
payload, depend on the types through generics or a user-supplied interpolator —
never reach into `UnityEngine`.

## Editor

Flat — three files, not worth subdividing.

```
Editor/
├── MonoEntityEditor.cs
├── RuntimeAspectDrawer.cs
├── AspectUsageAnalyzer.cs
├── ACS.Editor.asmdef
└── csc.rsp
```

## Tests

Mirror of `Runtime/` for navigation parity. Tests for pure classes live under
`Core/`, tests that actually need Unity under `Unity/`. `Integration/` is the
exception — cross-tier scenarios that don't belong to a single class.

```
Tests/
├── Core/
│   ├── Aspects/
│   │   └── AspectInjectorTests.cs
│   ├── Entities/
│   │   ├── EntityTests.cs
│   │   └── EntityLogicAttachTests.cs
│   └── World/
│       ├── WorldCoreTests.cs
│       ├── EntityRegistryTests.cs
│       └── EntityWorldCoreAutoWireTests.cs
├── Unity/
│   ├── Entities/
│   │   └── MonoEntityTests.cs
│   ├── Behavior/
│   │   ├── EntityComponentTests.cs
│   │   ├── EntityInjectorTests.cs
│   │   └── EntityTickRunnerTests.cs
│   └── World/
│       └── WorldTests.cs
├── Integration/
│   └── PureCoreIntegrationTests.cs
├── Editor/
│   └── AspectUsageAnalyzerTests.cs    # only if gated to Editor platform
└── ACS.Tests.asmdef
```

## Migration notes

- Keep the flat `Rubickanov.ACS.Runtime` namespace — physical folders do not
  need to map to namespaces, and leaving it flat keeps consumer `using`s
  unchanged.
- Assembly stays single: `ACS.Runtime`. A future split into
  `ACS.Core` (non-Unity) + `ACS.Runtime` (Unity adapters) stays on the table
  as a separate migration.
- Asmdef location stays at `Runtime/ACS.Runtime.asmdef` — its scope covers
  every subfolder automatically.
