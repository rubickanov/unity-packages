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

### Stable aspect keys

By default an aspect is keyed in the snapshot by `Type.FullName`. A rename or namespace move breaks old saves — the resolver can no longer find the type. Two attributes cover this:

```csharp
[PersistedKey("hero")]
[PersistedAlias("Game.Old.HeroAspect")]
public sealed class HeroAspect : IEntityAspect
{
    [PersistedState] public readonly ReactiveProperty<float> Health = new(100f);
}
```

- `[PersistedKey]` — canonical key written by `Snapshot()`. Without it the package falls back to `Type.FullName`, so existing saves keep loading.
- `[PersistedAlias]` — resolve-only. Multiple attributes chain renames across several migrations. Snapshots never write alias keys.

Alias resolution is a one-shot assembly scan cached for the session. A duplicate key (two aspects claiming the same `[PersistedKey]`, or an alias shadowing another aspect's canonical key) logs an error at first resolve — fix the collision, ACS picks one deterministically.

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

world.RestoreAll(
    snap,
    resolveOrSpawn,
    new WorldRestoreOptions { Migrations = migrations });
```

Each migrator advances exactly one step (`FromVersion` → `FromVersion + 1`); the registry composes the chain. `Snapshot()` stamps `AspectData.Version` from `[PersistedVersion]`; without the attribute the version is `0`, matching the pre-1.2 shape. A missing step or a snapshot written by newer code than the current aspect logs a warning and skips that aspect — one broken migration does not poison the whole restore.

Collection migrations stay inside the aspect migrator — `data.Fields[name]` is a regular `List<T>` / `Dictionary<K,V>` / `HashSet<T>` and can be rewritten freely. What the package **cannot** bridge is a change in the CLR shape of a collection *element* (struct fields added/removed) — the serializer handles drift there, not ACS.

### Cross-aspect migrations

Aspect splits, merges, and renames that cross type boundaries run at the `WorldSnapshot` layer, keyed by `FormatVersion`:

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
var snap = world.SnapshotAll(keyOf, migrations);

// Load — registry walks from snap.FormatVersion up to CurrentFormatVersion.
world.RestoreAll(snap, resolveOrSpawn,
    new WorldRestoreOptions { Migrations = migrations });
```

Snapshot migrators run once per entity snapshot *and* on the world-scoped slot before per-aspect migrators fire, so downstream migrators see the rearranged shape.

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

- **ByName (default)** — snapshot stores the member name. Safe against reorders and inserts; a rename requires an `IAspectMigrator`. Unknown names on restore log a warning and keep the field at its current value.
- **ByValue** — snapshot stores the underlying numeric. Compact; reorders or new members inserted before existing ones break old saves. Undefined values on restore log a warning and keep the current value.

Enum elements inside collections (`ObservableList<MyEnum>` etc.) are rejected in the current iteration — wrap the enum in a plain `int` or `string` on the aspect, or file an issue with the use case.

### Nullable value types

`ReactiveProperty<int?>` and similar nullable shapes are allowed. They forward to the underlying serializer; `null` survives the round-trip as long as the serializer preserves it (JsonUtility does not — Newtonsoft / MsgPack do). Aspect migrators can freely read and write `null` into the `AspectData.Fields` dictionary.

### Diagnostics — `PersistenceDebug`

Bootstrap validation and inspector-style dumps live on `PersistenceDebug`:

```csharp
// Fail fast in a bootstrap assertion.
PersistenceDebug.ValidateAspect<HeroAspect>();

// Scan a whole assembly — returns the full list of scanner rejections.
IReadOnlyList<string> errors = PersistenceDebug.ValidateAllAspects(typeof(HeroAspect).Assembly);
if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));

// Dump the resolved reverse index (key → type, version, aliases).
foreach (var entry in PersistenceDebug.ListPersistedKeys())
    Debug.Log($"{entry.Key} → {entry.Type.FullName} v{entry.Version}");

// Catch key collisions before they surface at restore.
foreach (var c in PersistenceDebug.FindKeyCollisions())
    Debug.LogError($"key '{c.Key}' is claimed by {string.Join(", ", c.Claimants.Select(t => t.Name))}");

// Human-readable per-aspect dump for inspector previews / crash payloads.
Debug.Log(PersistenceDebug.DumpAspect(typeof(HeroAspect)));
```

None of these mutate the scanner cache or the reverse index — safe to call from editor tooling, unit tests, or dev-build overlays.

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
- **Schema migration is opt-in, in two layers** — `IAspectMigrator` evolves one aspect's fields across a `[PersistedVersion]` bump; `IAspectSnapshotMigrator` restructures whole `AspectSnapshot` instances (split, merge, delete) keyed by `WorldSnapshot.FormatVersion`. Both are configured through a `PersistenceMigrationRegistry` owned by the save layer. The package ships the mechanism; the save layer supplies policy and concrete migrators. Collection-element CLR-shape drift stays with the save-layer serializer — ACS receives already-deserialized `List<T>` and cannot patch elements it never saw unboxed.
- **Restore writes through `ReactiveProperty.Value`** — no suppress flag. A restore is supposed to look like a normal write so netcode replication, UI bindings, and gameplay rules respond exactly as they do during live play.
- **Snapshot iteration is deterministic** — `AspectSnapshot.Aspects` and `AspectData.Fields` are backed by a `SortedDictionary<string, …>` with `StringComparer.Ordinal`. Identical state produces identical key ordering across runtimes and cultures, which is what autosave dedup (hash the serialized blob, skip the write if unchanged) and byte-wise save-file equality require. Determinism of the final serialized bytes is still the save layer's concern — a serializer that walks `IDictionary` in key order (Newtonsoft, MsgPack) inherits it for free; one that doesn't (`JsonUtility` via a wrapper, custom binary) must preserve it itself.
- **Aspect keys are CLR-type-specific, not polymorphic** — the registry resolves a snapshot key to exactly one concrete `Type`. A derived aspect with its own `[PersistedKey]` is a different save slot from its base; a shared key across a base/derived pair is a collision, not an inheritance chain. Polymorphism is not supported by design — if two aspect shapes need to coexist in the same save, give them distinct keys and pick between them in the save layer.
