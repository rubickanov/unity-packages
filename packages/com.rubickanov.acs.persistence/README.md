# ACS Persistence

Snapshot and restore for ACS aspects. Collects `[PersistedState]` fields into a detachable POCO and writes it back — no storage backend, no save slots. Stable aspect keys and two-layer schema migration are built in.

## Dependencies

- `com.rubickanov.acs` — the aspect framework this snapshots
- `R3` — `ReactiveProperty<T>`, the scalar field shape
- `ObservableCollections` — `ObservableList` / `ObservableDictionary` / `ObservableHashSet` field shapes

Unity 6000.0+.

## Architecture

```
IEntity.Snapshot()                 World.SnapshotAll(keyOf)
        │                                   │
        ▼                                   ▼
   AspectSnapshot  ────────────────►  WorldSnapshot
   { key → AspectData }               { World, Entities, FormatVersion }
        │                                   │
   AspectData                          save layer serializes + stores
   { Version, Fields }                      │
        ▲                                   ▼
   IEntity.Restore()  ◄───────────  World.RestoreAll(snap, resolveOrSpawn)
        │
   IAspectSnapshotMigrator  →  IAspectMigrator  →  ReactiveProperty.Value = …
```

The package produces and consumes plain value objects. Serialization, identity, prefab resolution, and storage all live in the game's save layer.

## Core Concepts

**Persisted field** — a `ReactiveProperty<T>`, `ObservableList<T>`, `ObservableHashSet<T>`, or `ObservableDictionary<K,V>` on an aspect, tagged `[PersistedState]`. Everything else on the aspect is runtime-only and never enters a snapshot.

**AspectData / AspectSnapshot / WorldSnapshot** — the three POCO layers. `AspectData` holds one aspect's field values (boxed, by field name) plus a schema `Version`. `AspectSnapshot` maps a stable key to `AspectData` for one entity. `WorldSnapshot` bundles every entity's snapshot plus the world-scoped slot and a `FormatVersion`.

**Snapshot key** — the stable id an aspect is stored under. `[PersistedKey]` when present, `Type.FullName` otherwise. Decoupling the key from the CLR type lets aspects be renamed or moved without breaking old saves.

**Migration layers** — `IAspectMigrator` evolves one aspect's fields across a `[PersistedVersion]` bump; `IAspectSnapshotMigrator` restructures whole snapshots (split, merge, delete aspects) across a `WorldSnapshot.FormatVersion` bump. Both are registered on a save-layer-owned `PersistenceMigrationRegistry`.

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
entity.Restore(snap);                       // apply
```

## Usage

### Marking persisted fields

`[PersistedState]` works on:

- `ReactiveProperty<T>` — where `T` is a value type or `string`.
- `ObservableList<T>` — same type rule for `T`.
- `ObservableHashSet<T>` — same type rule for `T`.
- `ObservableDictionary<K, V>` — both `K` and `V` must be value type or `string`.

Anything else logs an error at scan time and is skipped. Reference-type graphs are the save layer's concern, not ACS's. Fields declared on a base aspect class are included in derived aspects' snapshots — the scanner walks the type hierarchy explicitly.

A field tagged with both `[PersistedState]` and `[Replicated]` is fine — the two scanners are independent and own different pipelines.

### Snapshot and restore

```csharp
AspectSnapshot snap = entity.Snapshot();
entity.Restore(snap);
bool hasAny = entity.HasPersistedState();
```

`Snapshot()` returns a detachable POCO. Aspects with no `[PersistedState]` field are omitted entirely. `Restore()` creates missing aspects via `IEntity.Require<T>()` and writes the values back — writes go through the normal `ReactiveProperty.Value` setter, so UI, rules, and netcode replication react as they would at runtime.

Unknown fields in the snapshot are silently ignored; missing fields keep whatever default the aspect constructor set. Unknown aspect keys (removed or renamed since the snapshot was taken) log a warning and are skipped — the forward/backward compatibility the format provides by default.

### Stable aspect keys

By default an aspect is keyed by `Type.FullName`, so a rename or namespace move breaks old saves. Two attributes cover this:

```csharp
[PersistedKey("hero")]
[PersistedAlias("Game.Old.HeroAspect")]
public sealed class HeroAspect : IEntityAspect
{
    [PersistedState] public readonly ReactiveProperty<float> Health = new(100f);
}
```

- `[PersistedKey]` — canonical key written by `Snapshot()`. Without it the package falls back to `Type.FullName`, so existing saves keep loading.
- `[PersistedAlias]` — resolve-only. Apply it multiple times to chain renames across several migrations. Snapshots never write alias keys.

Alias resolution is a one-shot assembly scan cached for the session. A duplicate key — two aspects claiming the same `[PersistedKey]`, or an alias shadowing another aspect's canonical key — logs an error at first resolve; first registration wins deterministically.

### Per-aspect versioning and migrations

When a field is renamed, its type changes, or a new field needs a computed default, bump the aspect's version and supply a migrator:

```csharp
[PersistedKey("hero")]
[PersistedVersion(1)]
public sealed class HeroAspect : IEntityAspect
{
    [PersistedState] public readonly ReactiveProperty<float> Health = new(100f);
    [PersistedState] public readonly ReactiveProperty<int> ManaMax = new(0);
    [PersistedState] public readonly ReactiveProperty<int> Level = new(1);
}

public sealed class HeroV0ToV1 : IAspectMigrator
{
    public string AspectKey => "hero";
    public int FromVersion => 0;

    public void Migrate(AspectData data)
    {
        if (data.Fields.TryGetValue("HP", out var hp))
        {
            data.Fields["Health"] = hp;
            data.Fields.Remove("HP");
        }

        var level = data.Fields.TryGetValue("Level", out var lv) ? (int)lv : 1;
        data.Fields["ManaMax"] = level * 10;
    }
}

var migrations = new PersistenceMigrationRegistry()
    .AddAspect(new HeroV0ToV1());

entity.Restore(snap, migrations);
```

Each migrator advances exactly one step (`FromVersion` → `FromVersion + 1`); the registry composes the chain. `Snapshot()` stamps `AspectData.Version` from `[PersistedVersion]`; without the attribute the version is `0`. A missing step, or a snapshot written by newer code than the current aspect, logs a warning and skips that aspect — one broken migration does not poison the whole restore.

Collection migrations stay inside the aspect migrator — `data.Fields[name]` is a regular `List<T>` / `Dictionary<K,V>` / `HashSet<T>` and can be rewritten freely. What the package cannot bridge is a change in the CLR shape of a collection *element* (struct fields added or removed) — that drift is the serializer's job, not ACS's.

### Cross-aspect migrations

Aspect splits, merges, and renames that cross type boundaries run at the snapshot layer, keyed by `FormatVersion`:

```csharp
public sealed class SplitHealthMigrator : IAspectSnapshotMigrator
{
    public int FromFormatVersion => 0;

    public void Migrate(AspectSnapshot snap)
    {
        if (!snap.Aspects.Remove("legacy.health", out var legacy)) return;

        var health = new AspectData();
        if (legacy.Fields.TryGetValue("Health", out var h)) health.Fields["Value"] = h;
        snap.Aspects["game.health"] = health;

        var shield = new AspectData();
        if (legacy.Fields.TryGetValue("Shield", out var s)) shield.Fields["Value"] = s;
        snap.Aspects["game.shield"] = shield;
    }
}

var migrations = new PersistenceMigrationRegistry()
    .AddSnapshot(new SplitHealthMigrator());

// Save — registry stamps FormatVersion = 1 automatically.
WorldSnapshot snap = world.SnapshotAll(keyOf, migrations);

// Load — registry walks from snap.FormatVersion up to CurrentFormatVersion.
world.RestoreAll(snap, resolveOrSpawn,
    new WorldRestoreOptions { Migrations = migrations });
```

Snapshot migrators run once per entity snapshot and on the world-scoped slot before per-aspect migrators fire, so downstream migrators see the rearranged shape.

### Whole-world snapshots

`SnapshotAll` captures every persisted entity in the world — plus the world-scoped aspects — in a single detachable `WorldSnapshot`. `RestoreAll` writes it back.

```csharp
WorldSnapshot snap = world.SnapshotAll(e => saveLayer.IdOf(e));

world.RestoreAll(snap, id => saveLayer.ResolveOrSpawn(id));
```

- `keyOf` is invoked for every non-world entity; it must return a non-null, unique id — a `null` return or a duplicate key throws.
- `resolveOrSpawn` either looks up an existing entity by the stored id or spawns a new one (prefab resolution is the save layer's concern). Returning `null` surfaces a warning and skips that entry; returning the `World` itself logs an error and is skipped.
- World-scoped aspects live on the dedicated `WorldSnapshot.World` slot — `keyOf` is never called on the world, and `DisposeMissing` never touches it.

### Missing-entity policy

`RestoreAll` takes `WorldRestoreOptions` to control entities that are alive in the world but absent from the snapshot:

```csharp
world.RestoreAll(
    snap,
    id => saveLayer.ResolveOrSpawn(id),
    new WorldRestoreOptions { Missing = MissingEntityPolicy.DisposeMissing });
```

- `MissingEntityPolicy.Ignore` (default) — leave them alone. Right for checkpoints and partial restores.
- `MissingEntityPolicy.DisposeMissing` — dispose every persisted entity not referenced by the snapshot. Right for "load slot from scratch". Entities without any `[PersistedState]` field (particles, runtime-only ownership aspects) survive; the world itself is never disposed.

Default teardown disposes `IDisposable` entities and calls `UnityEngine.Object.Destroy(component.gameObject)` for `Component`-backed ones such as `MonoEntity`. Supply `WorldRestoreOptions.DisposeMissing` to override — for example, to return pooled entities instead of destroying them.

### World-scoped aspects

`World` implements `IEntity`, so a `WorldTimeAspect` with `[PersistedState]` on `TimeOfDay` snapshots and restores through the same API:

```csharp
var world = new World();
((IEntity)world).Require<WorldTimeAspect>().TimeOfDay.Value = 12.5f;

AspectSnapshot worldSnap = ((IEntity)world).Snapshot();
```

In a `WorldSnapshot` these land on the `World` slot, never inside `Entities`.

### Walking every persisted entity

For custom workflows that don't fit `SnapshotAll` / `RestoreAll`, iterate directly:

```csharp
foreach (var entity in world.PersistedEntities())
{
    AspectSnapshot snap = entity.Snapshot();
    // Save layer decides the id, the prefabId, the storage, and the timing.
}
```

### Enum fields

Enums are opt-in — the scanner rejects `ReactiveProperty<MyEnum>` unless the field also carries `[PersistedEnum]`. The encoding choice has save-stability implications and must be explicit:

```csharp
public enum Stance { Neutral, Aggressive, Defensive }

public sealed class CombatAspect : IEntityAspect
{
    [PersistedState] [PersistedEnum]                              // ByName (default) — stored as "Aggressive"
    public readonly ReactiveProperty<Stance> Stance = new(Stance.Neutral);

    [PersistedState] [PersistedEnum(PersistedEnumMode.ByValue)]   // stored as the underlying int
    public readonly ReactiveProperty<Stance> Preferred = new(Stance.Neutral);
}
```

- `PersistedEnumMode.ByName` (default) — stores the member name. Safe against reorders and inserts; a rename needs an `IAspectMigrator`. Unknown names on restore log a warning and keep the field's current value.
- `PersistedEnumMode.ByValue` — stores the underlying numeric. Compact, but reorders or members inserted before existing ones break old saves. Undefined values on restore log a warning and keep the current value.

Enum elements inside collections (`ObservableList<MyEnum>` etc.) are rejected — wrap the enum as a plain `int` or `string` on the aspect.

### Nullable value types

`ReactiveProperty<int?>` and similar nullable shapes are allowed. They forward to the underlying serializer; `null` survives the round-trip as long as the serializer preserves it (`JsonUtility` does not — Newtonsoft and MsgPack do). Aspect migrators can freely read and write `null` into the `AspectData.Fields` dictionary.

### Diagnostics

Bootstrap validation and inspector-style dumps live on `PersistenceDebug`. None of these mutate the scanner cache or the reverse index, so they are safe from editor tooling, tests, or dev-build overlays.

```csharp
// Fail fast in a bootstrap assertion.
PersistenceDebug.ValidateAspect<HeroAspect>();

// Scan a whole assembly — returns every scanner rejection.
IReadOnlyList<string> errors = PersistenceDebug.ValidateAllAspects(typeof(HeroAspect).Assembly);
if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));

// Dump the resolved reverse index (key → type, version, aliases).
foreach (PersistedKeyEntry entry in PersistenceDebug.ListPersistedKeys())
    Debug.Log($"{entry.Key} -> {entry.Type.FullName} v{entry.Version}");

// Catch key collisions before they surface at restore.
foreach (PersistedKeyCollision c in PersistenceDebug.FindKeyCollisions())
    Debug.LogError($"key '{c.Key}' claimed by {c.Claimants.Length} types");

// Human-readable per-aspect dump for inspector previews / crash payloads.
Debug.Log(PersistenceDebug.DumpAspect(typeof(HeroAspect)));
```

## Examples

### Renaming a field across one version

Save format v0 stored health under `HP`; v1 renames it to `Health` and adds a computed `ManaMax`. Bump `[PersistedVersion]` to `1`, register `HeroV0ToV1` (above), and pass the registry to `Restore` or `RestoreAll`. Old v0 saves migrate field-by-field on load; v1 saves skip the migrator entirely because their `AspectData.Version` already matches.

### Splitting one aspect into two

A legacy `legacy.health` aspect held both `Health` and `Shield`. `SplitHealthMigrator` (above) removes it and writes two new aspects keyed `game.health` and `game.shield`. Because this crosses type boundaries it runs at the snapshot layer via `IAspectSnapshotMigrator`, before any per-aspect migrator sees the data.

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

- **No storage, no slots, no autosave** — that's a product concern. Slot UI, cloud sync, platform save APIs, autosave timing, and checkpoint triggers live in the game's save layer.
- **No prefab resolution, no persistent identity** — ACS does not know what a prefab is and does not care about cross-session ids. The save layer maps between its own id scheme and whatever `IEntity` the runtime produced.
- **No built-in serializer** — `AspectData.Fields` holds boxed values keyed by field name. Any serializer (`JsonUtility`, Newtonsoft, MsgPack, binary) consumes it as-is; the save layer picks the format.
- **Schema migration is opt-in, in two layers** — `IAspectMigrator` evolves one aspect's fields across a `[PersistedVersion]` bump; `IAspectSnapshotMigrator` restructures whole snapshots across a `WorldSnapshot.FormatVersion` bump. Both are configured through a `PersistenceMigrationRegistry` owned by the save layer. The package ships the mechanism; the save layer supplies policy and concrete migrators.
- **Restore writes through `ReactiveProperty.Value`** — no suppress flag. A restore is supposed to look like a normal write so netcode replication, UI bindings, and gameplay rules respond exactly as they do during live play.
- **Snapshot iteration is deterministic** — `Aspects`, `Fields`, and `Entities` are backed by `SortedDictionary` with `StringComparer.Ordinal`. Identical state produces identical key ordering across runtimes and cultures, which autosave dedup and byte-wise save-file equality rely on. The final serialized bytes are still the save layer's concern — a serializer that walks `IDictionary` in key order inherits the determinism for free; one that does not must preserve it itself.
- **Aspect keys are CLR-type-specific, not polymorphic** — the registry resolves a snapshot key to exactly one concrete `Type`. A derived aspect with its own `[PersistedKey]` is a different save slot from its base; a shared key across a base/derived pair is a collision, not an inheritance chain. If two aspect shapes must coexist in one save, give them distinct keys and pick between them in the save layer.
</content>
</invoke>
