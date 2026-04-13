# ACS Netcode — Folder Structure

Every file in this package depends on `Unity.Netcode` (`NetworkBehaviour`,
`NetworkManager`, `CustomMessagingManager`, `FastBufferWriter`, …). There is
no meaningful "pure C#" core to extract — the grouping is purely by
**concern**, to keep the 30+ runtime files navigable.

## Runtime

```
Runtime/
├── Attributes/
│   ├── ReplicatedAttribute.cs
│   ├── ReplicatedEventAttribute.cs
│   └── NetworkScopeAttribute.cs
├── Authority/
│   ├── AuthorityMode.cs
│   ├── NetworkScope.cs
│   ├── NetworkScopeScanner.cs
│   └── AuthorityRenderBinding.cs
├── Replication/
│   ├── AspectReplicator.cs            # per-entity NetworkBehaviour
│   ├── AspectReplicationSystem.cs     # one-per-NetworkManager orchestrator
│   ├── ReplicationScanner.cs
│   ├── AotHints.cs
│   ├── Fields/
│   │   ├── ReplicatedFieldBinding.cs
│   │   ├── InterpolatedFieldBinding.cs
│   │   ├── InterpolationMode.cs
│   │   ├── InterpolationRegistry.cs
│   │   └── Interpolators.cs
│   └── Events/
│       ├── ReplicatedEventBinding.cs
│       ├── IEventBroadcaster.cs
│       └── Reliability.cs
├── Prediction/
│   ├── ISimulate.cs
│   ├── IInputCommand.cs
│   ├── IInputProvider.cs
│   ├── InputBuffer.cs
│   ├── SnapshotBuffer.cs
│   ├── PredictionManager.cs
│   └── PredictionScanner.cs
├── Entities/
│   └── EntityNetworkComponent.cs      # NetworkBehaviour + IEntityComponent
├── Extensions/
│   └── ReactivePropertyExtensions.cs
├── ACS.Runtime.Netcode.asmdef
├── AssemblyInfo.cs
└── csc.rsp
```

### What lives where

- **`Attributes/`** — the three user-facing attributes. `[Replicated]`,
  `[ReplicatedEvent]`, `[NetworkScope]`. First place a consumer looks.
- **`Authority/`** — who-writes-what decisions. `AuthorityMode` (server/owner),
  `NetworkScope` (everywhere/server-only/owner-only) and its scanner, plus
  `AuthorityRenderBinding` which gates render-side state by authority.
- **`Replication/`** — the guts. The replicator per-entity, the
  per-NetworkManager orchestrator, the scanner that walks aspects at spawn.
  Split into `Fields/` (state: `ReactiveProperty<T>` + interpolation) and
  `Events/` (`Subject<T>` broadcasts). `AotHints` sits at the root because it
  covers both.
- **`Prediction/`** — client-side prediction + reconciliation. Inputs,
  snapshot ring buffer, `PredictionManager<TInput>`, scanner for
  `[Replicated(Predicted = true)]`.
- **`Entities/`** — `EntityNetworkComponent`, the `NetworkBehaviour`-based
  equivalent of `EntityComponent` for components that need RPCs / ownership
  checks directly.
- **`Extensions/`** — small R3-adjacent helpers. Separate so they don't clutter
  replication files.

### Why `Replication/Fields/` + `Replication/Events/` and not flat

`AspectReplicator` and `AspectReplicationSystem` already span ~1400 lines
combined and touch both fields and events. Separating the binding types into
sibling folders keeps the per-binding detail (interpolation, reliability, etc.)
out of the way when reading the orchestrator, and gives each side room to grow
— field bindings will likely sprout managed-type support (`string`,
`INetworkSerializable`), events may add relevancy filters.

### Related design docs (stay at package root)

```
DESIGN.md                              # full layer breakdown
DELTA-COMPRESSION-AND-RELEVANCY.md     # planned
LAG-COMPENSATION.md                    # planned
ISSUES.md                              # audit findings
README.md
```

Design docs stay at the root, not under `Runtime/Replication/` etc. — they
describe the package as a whole and cross-cut multiple folders.

## Tests

Mirror of `Runtime/` where it makes sense; integration tests live in their
own subtree because they exercise the full pipeline (spawn → replicate →
apply → reconcile) and don't belong to a single binding class.

```
Tests/
└── Runtime/
    ├── Replication/
    │   ├── ReplicationScannerTests.cs
    │   └── Fields/
    │       └── ...
    ├── Prediction/
    │   └── PredictionScannerTests.cs
    ├── Authority/
    │   └── AuthorityRenderBindingTests.cs
    ├── Entities/
    │   └── EntityNetworkComponentLifecycleTests.cs
    └── Integration/
        ├── AspectReplicatorIntegrationTestBase.cs
        ├── AspectReplicatorLifecycleTests.cs
        ├── PredictionPipelineTests.cs
        ├── TestAspects.cs
        └── MonsterStateAspect.cs
```

## Migration notes

- Keep the flat `Rubickanov.ACS.Runtime.Netcode` namespace — physical folders
  do not need to map to namespaces.
- Asmdef location stays at `Runtime/ACS.Runtime.Netcode.asmdef` — its scope
  covers every subfolder.
- A future extraction of a pure-C# simulation core (e.g. `ISimulate`,
  `IInputCommand`, `InputBuffer`, `SnapshotBuffer` minus the NGO wiring) into
  its own assembly remains an open option, but is out of scope for the
  folder reshuffle.
