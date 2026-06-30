# Gameplay Tags

Hierarchical tag system for categorization, filtering, and matching. Index-based 4-byte struct tags with parent-chain matching and code generation.

## Dependencies

None.

## Architecture

```
GameplayTagAsset (ScriptableObject database)
        │
        │ BuildRegistry()
        ▼
GameplayTagRegistry (singleton, owns hierarchy)
        │
        │ Get() / Matches()
        ▼
GameplayTag (readonly struct, 4 bytes)
        │
        │ collected in
        ▼
GameplayTagContainer (sorted list, hierarchical queries)
```

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **GameplayTags.Runtime** | Yes | Core types: GameplayTag, GameplayTagRegistry, GameplayTagContainer. Uses `UnityEngine` only for play-mode static reset. |
| **GameplayTags.Unity** | Yes | GameplayTagAsset, SerializedGameplayTag, SerializedGameplayTagContainer |
| **GameplayTags.Editor** | Editor | Dropdown picker, database inspector, code generator |

## Core Concepts

**GameplayTag** — Readonly struct identified by an integer index into the registry. Supports hierarchical matching via `Matches()`: `"Damage.Fire.DoT".Matches("Damage")` returns true.

**GameplayTagRegistry** — Singleton that owns the tag hierarchy, name-to-index mapping, and parent-chain walk. Must be installed at startup. Main-thread only. Additive: call `AddTags(...)` after install to append new tags (parent tags are auto-created). Existing indices remain stable, so cached `GameplayTag` values and generated constants keep working.

**GameplayTagContainer** — Mutable sorted collection of tags with `HasTag()` (hierarchical) and `HasTagExact()` (exact) queries. O(log n) exact lookups, O(n * depth) hierarchical queries. Mutating during enumeration throws `InvalidOperationException`.

**ReadOnlyGameplayTagContainer** — Immutable view over a `GameplayTagContainer`. Used as the return type for accessors owned by other objects (e.g. `SerializedGameplayTagContainer.Container`) so callers can query but not mutate.

## Quick Start

1. Create one or more tag databases: **Assets > Create > Config > Gameplay Tags**. You can split tags across multiple assets by category (e.g. `DamageTags`, `StatusTags`).
2. Add tags in the inspector (e.g. `Damage.Fire.DoT`, `Status.Stun`).
3. Install the registry at startup:

```csharp
[SerializeField] private GameplayTagAsset _tagDatabase = default!;

void Awake()
{
    GameplayTagRegistry.Install(_tagDatabase.BuildRegistry());
}
```

4. Generate constants: **Tools > Generators > Gameplay Tags**. The generator finds all `GameplayTagAsset` files in the project, merges their tags, and produces a single output file.

### Adding Tags at Runtime

Call `AddTags` to append more tags after install — useful for DLC, mod content, or async content loading:

```csharp
GameplayTagRegistry.Instance.AddTags(dlcTagAsset.TagPaths);
```

Existing indices are preserved, so previously cached `GameplayTag` values and generated constants remain valid. Invalid paths throw `ArgumentException`.

## Usage

### Hierarchical Matching

`Matches()` returns true if the tag equals or descends from the query:

```csharp
var fireDoT = GameTags.Damage.Fire.DoT;

fireDoT.Matches(GameTags.Damage.Tag);       // true — DoT is a kind of Damage
fireDoT.Matches(GameTags.Damage.Fire.Tag);  // true — DoT is a kind of Fire
fireDoT.Matches(GameTags.Damage.Ice.Tag);   // false — not Ice
```

### Container Queries

**GameplayTagContainer** supports hierarchical and exact queries:

```csharp
var tags = GameplayTagContainer.From(GameTags.Status.Stun, GameTags.Damage.Fire.DoT);

tags.HasTag(GameTags.Damage.Tag);       // true — DoT descends from Damage
tags.HasTagExact(GameTags.Damage.Tag);  // false — exact Damage not in container
```

`HasTag()` semantics: returns true if any contained tag equals or descends from the query.

| Container | Query | HasTag | HasTagExact |
|-----------|-------|--------|-------------|
| `[Damage.Fire.DoT]` | `Damage` | true | false |
| `[Damage]` | `Damage.Fire` | false | false |
| `[Damage.Fire]` | `Damage.Fire` | true | true |

### Multi-Tag Queries

```csharp
var active = GameplayTagContainer.From(GameTags.Status.Stun, GameTags.Status.Slow);
var required = GameplayTagContainer.From(GameTags.Status.Stun);
var blocked = GameplayTagContainer.From(GameTags.Status.Immune.Tag);

active.HasAll(required);  // true — all required tags satisfied
active.HasAny(blocked);   // false — no blocked tags present
```

Exact variants: `HasAllExact()`, `HasAnyExact()`.

### Inspector Fields

Use **SerializedGameplayTag** and **SerializedGameplayTagContainer** for Inspector-configurable tags with dropdown pickers:

```csharp
[SerializeField] private SerializedGameplayTag _damageType;
[SerializeField] private SerializedGameplayTagContainer _immunities;

void OnEnable()
{
    GameplayTag tag = _damageType.Tag;
    ReadOnlyGameplayTagContainer immune = _immunities.Container;
}
```

### Code Generation

Generated constants mirror the tag hierarchy as nested static classes:

```csharp
public static class GameTags
{
    public static class Damage
    {
        public static readonly GameplayTag Tag = GameplayTagRegistry.Instance.Get("Damage");

        public static class Fire
        {
            public static readonly GameplayTag Tag = GameplayTagRegistry.Instance.Get("Damage.Fire");
            public static readonly GameplayTag DoT = GameplayTagRegistry.Instance.Get("Damage.Fire.DoT");
        }
    }
}
```

Configure output path, namespace, class name, access modifier (`public`/`internal`), and whether the class is `partial` in **Project Settings > Gameplay Tags Generator**. Auto-regeneration triggers when any tag database asset is modified. If multiple `GameplayTagAsset` files exist, all are merged during generation and in the Inspector dropdown picker.

## Examples

### Ability Gating

```csharp
[SerializeField] private SerializedGameplayTagContainer _requiredTags;
[SerializeField] private SerializedGameplayTagContainer _blockedByTags;

public bool CanActivate(GameplayTagContainer activeTags)
{
    if (!activeTags.HasAll(_requiredTags.Container))
        return false;

    if (activeTags.HasAny(_blockedByTags.Container))
        return false;

    return true;
}
```

### Weakness Table

```csharp
var weaknesses = GameplayTagContainer.From(
    GameTags.Damage.Fire.Tag,
    GameTags.Damage.Ice.Tag
);

// Hierarchical — Fire.DoT is a child of Fire, so it matches
if (weaknesses.HasTag(incomingDamageType))
    finalDamage *= 1.5f;
```

## Design Decisions

- **Index-based struct (4 bytes)** — tags are compared by integer index, not string. Zero allocation at runtime after registry installation.
- **Singleton registry with Install/Uninstall** — tags are meaningless without a hierarchy. The registry must exist before any tag operations. `Uninstall()` exists primarily for tests; in production, prefer `AddTags(...)` to extend the live registry and keep existing indices stable. `Install`/`AddTags`/`Instance` are main-thread-only.
- **Serialized wrappers store string paths** — `SerializedGameplayTag` persists the dot-separated path, not the index. Paths are stable; cached indices are re-resolved lazily after deserialize.
- **`SerializedGameplayTagContainer.Container` returns a read-only view** — mutation must go through the `_paths` serialized field (editor) or by rebuilding the wrapper. The read-only view prevents aliasing issues that would otherwise arise from the struct's shared backing container.
- **Runtime depends only on `UnityEngine` for play-mode static reset** — the tag, registry, and container types are otherwise plain C#. `[RuntimeInitializeOnLoadMethod]` clears the registry singleton between play sessions when domain reload is disabled.
