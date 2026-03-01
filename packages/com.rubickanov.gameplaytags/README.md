# Gameplay Tags

Hierarchical gameplay tag system for categorization, filtering, and matching. Inspired by Unreal Engine's FGameplayTag/FGameplayTagContainer.

## Features

- **Hierarchical matching** — `"Damage.Fire.DoT".Matches("Damage")` returns true
- **4-byte struct** — `GameplayTag` is a lightweight index-based readonly struct
- **Zero dependencies** — Runtime assembly has no engine references (pure C#)
- **Inspector support** — dropdown picker, container drawer, database editor
- **Code generation** — strongly-typed static constants from tag database

## Architecture

| Assembly | Description |
|----------|-------------|
| `GameplayTags.Runtime` | Pure C# — `GameplayTag`, `GameplayTagRegistry`, `GameplayTagContainer` |
| `GameplayTags.Unity` | UnityEngine — `GameplayTagAsset`, `SerializedGameplayTag`, `SerializedGameplayTagContainer` |
| `GameplayTags.Editor` | Editor — property drawers, database inspector, code generator |

## Setup

1. Create a tag database: **Assets > Create > Config > Gameplay Tags**
2. Add tags in the inspector (e.g. `Damage.Fire.DoT`, `Status.Stun`)
3. Install the registry at startup:

```csharp
[SerializeField] private GameplayTagAsset _tagDatabase = default!;

void Awake()
{
    GameplayTagRegistry.Install(_tagDatabase.BuildRegistry());
}
```

4. Generate constants: **Tools > Generators > Gameplay Tags**

## Usage

### Runtime queries

```csharp
// Hierarchical matching
if (damageTag.Matches(GameTags.Damage.Tag))        // any damage
if (damageTag.Matches(GameTags.Damage.Fire.Tag))    // fire damage specifically

// Container — manual construction
var tags = new GameplayTagContainer();
tags.AddTag(GameTags.Status.Stun);
tags.AddTag(GameTags.Damage.Fire.DoT);

// Container — factory method
var tags = GameplayTagContainer.From(GameTags.Status.Stun, GameTags.Damage.Fire.DoT);

tags.HasTag(GameTags.Damage.Tag);       // true — DoT descends from Damage
tags.HasTagExact(GameTags.Damage.Tag);  // false — exact Damage not in container
tags.ToString();                        // "[Damage.Fire.DoT, Status.Stun]"
```

### Inspector fields

```csharp
[SerializeField] private SerializedGameplayTag _requiredTag;
[SerializeField] private SerializedGameplayTagContainer _immunities;

void OnEnable()
{
    GameplayTag tag = _requiredTag.Tag;
    GameplayTagContainer immune = _immunities.Container;
}
```

## HasTag semantics

`container.HasTag(query)` returns true if the container holds **any tag that equals or descends from** the query.

| Container | Query | Result |
|-----------|-------|--------|
| `["Damage.Fire.DoT"]` | `Damage` | `true` |
| `["Damage"]` | `Damage.Fire` | `false` |
| `["Damage.Fire"]` | `Damage.Fire` | `true` |

## Examples

### Damage type checking

```csharp
// Tag hierarchy: Damage > Damage.Fire > Damage.Fire.DoT
// Hierarchical match — DoT is a kind of Fire damage
var hitType = GameTags.Damage.Fire.DoT;
hitType.Matches(GameTags.Damage.Tag);       // true — any damage
hitType.Matches(GameTags.Damage.Fire.Tag);  // true — fire damage
hitType.Matches(GameTags.Damage.Ice.Tag);   // false — not ice
```

### Status / immunity checks

```csharp
var active = new GameplayTagContainer();
active.AddTag(GameTags.Status.Immune.Stun);
active.AddTag(GameTags.Status.Slow);

// Hierarchical — Immune.Stun satisfies Immune query
active.HasTag(GameTags.Status.Immune.Tag);  // true
// Exact — the container doesn't have bare Immune
active.HasTagExact(GameTags.Status.Immune.Tag);  // false
```

### Ability gating with Inspector-configured tags

```csharp
// Configured in Inspector via dropdown pickers
[SerializeField] private SerializedGameplayTagContainer _requiredTags;
[SerializeField] private SerializedGameplayTagContainer _blockedByTags;

public bool CanActivate(GameplayTagContainer activeTags)
{
    // All required tags must be present
    if (!activeTags.HasAll(_requiredTags.Container))
        return false;

    // None of the blocked tags may be present
    if (activeTags.HasAny(_blockedByTags.Container))
        return false;

    return true;
}
```

### Weakness / resistance table

```csharp
var weaknesses = GameplayTagContainer.From(
    GameTags.Damage.Fire.Tag,
    GameTags.Damage.Ice.Tag
);

// Hierarchical — Fire.DoT is a child of Fire, so it matches
if (weaknesses.HasTag(incomingDamageType))
    finalDamage *= 1.5f;
```

## Code generation

Generated constants follow the tag hierarchy as nested static classes:

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

Configure output path, namespace, and class name in **Project Settings > Gameplay Tags Generator**.

Auto-regeneration triggers when the tag database asset is modified.
