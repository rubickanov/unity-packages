# ACS Persistence

Snapshot/restore primitives for ACS aspects. Extension for [ACS](../com.rubickanov.acs/).

## Dependencies

- `com.rubickanov.acs` — base aspect framework
- `R3` — `ReactiveProperty<T>` underpinning persisted fields
- `ObservableCollections` — list/dictionary/hash-set support

## Quick Start

Mark persisted fields and call the extension methods.

```csharp
using ObservableCollections;
using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Persistence;

public sealed class PlayerAspect : IEntityAspect
{
    [PersistedState] public readonly ReactiveProperty<float> Health = new(100f);
    [PersistedState] public readonly ReactiveProperty<string> Name = new("unset");
    [PersistedState] public readonly ObservableList<int> Inventory = new();

    public readonly ReactiveProperty<bool> IsInCombat = new(false); // runtime-only
}

var entity = new Entity();
entity.Require<PlayerAspect>().Health.Value = 73f;

AspectSnapshot snap = entity.Snapshot();   // collect
entity.Restore(snap);                      // apply
```

## Usage

### Marking persisted fields

`[PersistedState]` works on:

- `ReactiveProperty<T>` — where `T` is a value type or `string`.
- `ObservableList<T>` — same type rule for `T`.
- `ObservableHashSet<T>` — same type rule for `T`.
- `ObservableDictionary<K, V>` — both `K` and `V` must be value type or `string`.

Anything else logs an error at scan time and is skipped. Reference-type graphs are the save layer's concern, not ACS's.

A field tagged with both `[PersistedState]` and `[Replicated]` is fine — the two scanners are independent and own different pipelines.

### Snapshot and restore

```csharp
AspectSnapshot snap = entity.Snapshot();
entity.Restore(snap);
bool hasAny = entity.HasPersistedState();
```

`Snapshot()` returns a detachable POCO. Aspects without any `[PersistedState]` field are omitted entirely. `Restore()` creates missing aspects via `IEntity.Require<T>()` and writes the values back — writes go through the normal `ReactiveProperty.Value` setter, so UI, rules, and netcode replication all react as they would at runtime.

Unknown fields in the snapshot are silently ignored; missing fields keep whatever default the aspect constructor set. Unknown aspect types (removed/renamed since the snapshot was taken) log a warning and are skipped — this is the forward/backward compatibility the format provides by default.

### World-scoped aspects

`World` implements `IEntity`, so a `WorldTimeAspect` with `[PersistedState]` on `TimeOfDay` snapshots and restores through the same API:

```csharp
var world = new World();
((IEntity)world).Require<WorldTimeAspect>().TimeOfDay.Value = 12.5f;

AspectSnapshot worldSnap = ((IEntity)world).Snapshot();
```

### Whole-world snapshots

`SnapshotAll` captures every persisted entity in the world — plus the world-scoped aspects — in a single detachable `WorldSnapshot`. `RestoreAll` writes it back.

```csharp
WorldSnapshot snap = world.SnapshotAll(e => saveLayer.IdOf(e));

world.RestoreAll(snap, id => saveLayer.ResolveOrSpawn(id));
```

- `keyOf` is invoked for every non-world entity; it must return a non-null, unique id — a `null` return or a duplicate key throws.
- `resolveOrSpawn` either looks up an existing entity by the stored id or spawns a new one (prefab resolution is the save layer's concern). Returning `null` surfaces a warning and skips that entry.
- World-scoped aspects live on the dedicated `WorldSnapshot.World` slot — `keyOf` is never called on the world, `DisposeMissing` never touches it.

#### Missing-entity policy

`RestoreAll` accepts `WorldRestoreOptions` to control what happens to entities that are alive in the world but absent from the snapshot:

```csharp
world.RestoreAll(
    snap,
    id => saveLayer.ResolveOrSpawn(id),
    new WorldRestoreOptions { Missing = MissingEntityPolicy.DisposeMissing });
```

- `MissingEntityPolicy.Ignore` (default) — leave them alone. Right for checkpoints and partial restores.
- `MissingEntityPolicy.DisposeMissing` — dispose every persisted entity not referenced by the snapshot. Right for "load slot from scratch". Entities without any `[PersistedState]` field (particles, runtime-only ownership aspects) survive; the world itself is never disposed.

Default teardown disposes `IDisposable` entities and calls `UnityEngine.Object.Destroy(component.gameObject)` for `MonoEntity`-backed ones. Supply `WorldRestoreOptions.DisposeMissing` to override — for example, to return pooled entities instead of destroying them.

### Walking every persisted entity

For custom workflows that don't fit `SnapshotAll` / `RestoreAll`, iterate directly:

```csharp
foreach (var entity in world.PersistedEntities())
{
    var snap = entity.Snapshot();
    // Save layer decides the id, the prefabId, the storage, and the timing.
}
```

## Integration

A save layer owns the remaining concerns. `com.rubickanov.acs.persistence` exposes snapshots; the game layer supplies identity, prefab resolution, and storage.

```csharp
public sealed class SaveService
{
    private readonly IStorage _storage;
    private readonly Func<IEntity, string> _getId;
    private readonly Func<string, IEntity> _resolveOrSpawn;

    public async Task SaveSlot(string slot)
    {
        WorldSnapshot snap = World.Current.SnapshotAll(_getId);
        await _storage.Write(slot, snap);
    }

    public async Task LoadSlot(string slot)
    {
        WorldSnapshot snap = await _storage.Read<WorldSnapshot>(slot);
        World.Current.RestoreAll(
            snap,
            _resolveOrSpawn,
            new WorldRestoreOptions { Missing = MissingEntityPolicy.DisposeMissing });
    }
}
```

## Design Decisions

- **No storage, no slots, no autosave** — that's a product concern. Slot UI, cloud sync, platform save APIs, autosave timing, and checkpoint triggers live in the game's save layer or a separate `com.rubickanov.save` package.
- **No prefab resolution, no persistent identity** — ACS does not know what a prefab is and does not care about stable cross-session ids. The save layer maps between its own id scheme and whatever `IEntity` the runtime produced.
- **No built-in serializer** — `AspectSnapshot` holds boxed values in a dictionary keyed by `Type.FullName`. Any serializer (`JsonUtility`, `Newtonsoft`, `MsgPack`, binary) consumes it as-is; the save layer picks the format.
- **No schema migration in v1** — versioning and migration paths will land when the first real consumer needs them; premature API surface would lock the shape of migration before the real use case is known.
- **Restore writes through `ReactiveProperty.Value`** — no suppress flag. A restore is supposed to look like a normal write so netcode replication, UI bindings, and gameplay rules respond exactly as they do during live play.
- **Snapshot iteration is deterministic** — `AspectSnapshot.Aspects` and `AspectData.Fields` are backed by a `SortedDictionary<string, …>` with `StringComparer.Ordinal`. Identical state produces identical key ordering across runtimes and cultures, which is what autosave dedup (hash the serialized blob, skip the write if unchanged) and byte-wise save-file equality require. Determinism of the final serialized bytes is still the save layer's concern — a serializer that walks `IDictionary` in key order (Newtonsoft, MsgPack) inherits it for free; one that doesn't (`JsonUtility` via a wrapper, custom binary) must preserve it itself.
