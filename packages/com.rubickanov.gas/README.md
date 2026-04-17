# Gameplay Ability System (GAS)

Data-driven gameplay effects system. Attribute modifiers with duration, periodicity, stacking, and tag-based conditions.

## Dependencies

- `com.rubickanov.gameplaytags` — tag-based attribute identification and effect conditions

## Architecture

```
GameplayEffectAsset (ScriptableObject)
        |
        v
    EffectDef (immutable definition)
        |
        v
    EffectSpec (runtime instance: def + source + magnitude)
        |
        v
  EffectController --> ActiveEffect (tracked handle, timers)
        |                    |
        v                    v
  AttributeSet <-- ModifierAggregator (recalculates CurrentValue)
```

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **GAS.Runtime** | No | Core logic — attributes, effects, aggregation. Pure C#. |
| **GAS.Unity** | Yes | `GameplayEffectAsset`, `SerializedModifier` — inspector integration |
| **GAS.Editor** | Editor | Custom inspector for effect assets, modifier property drawer |

## Core Concepts

**GameplayAttribute** — Holds a `BaseValue` and a computed `CurrentValue`. The controller recalculates `CurrentValue` whenever active effects change. Fires `ValueChanged` on change.

**Modifier** — Readonly struct describing a single operation on a single attribute: attribute tag, operation (`Add` / `Multiply` / `Override`), and value.

**EffectDef** — Immutable definition of an effect: duration policy, modifiers, tag conditions. Created from **GameplayEffectAsset** via `ToDef()` or built in code.

**EffectSpec** — Runtime instance pairing an **EffectDef** with a source object and magnitude scalar. Magnitude scales all modifier values.

**EffectController** — Owns **ActiveEffect** instances. Applies, removes, and ticks effects. Recalculates attributes on every change.

## Quick Start

Define your attribute / effect / status tags as constants via `com.rubickanov.gameplaytags` (the `Attribute.*`, `Status.*`, `Effect.*` identifiers used throughout this README are project-defined tags, not types the package exports):

```csharp
public static class Attribute
{
    public static readonly GameplayTag Health = GameplayTagRegistry.Instance.Get("Attribute.Health");
    public static readonly GameplayTag MoveSpeed = GameplayTagRegistry.Instance.Get("Attribute.MoveSpeed");
}
```

1. Define attributes and create a controller:

```csharp
var attributes = new AttributeSet();
attributes.Define(Attribute.Health, 100f);
attributes.Define(Attribute.MoveSpeed, 5f);

var tags = new GameplayTagContainer();
var controller = new EffectController(attributes, tags);
```

2. Create a **GameplayEffectAsset** via `Assets > Create > GAS > Gameplay Effect` in the Inspector.

3. Apply effects and tick the controller:

```csharp
[SerializeField] private GameplayEffectAsset _poisonEffect = default!;

var spec = _poisonEffect.CreateSpec(source: this, magnitude: 1f);
var handle = controller.ApplyEffect(spec);

// In update loop
controller.Tick(Time.deltaTime);
```

## Usage

### Defining Attributes

```csharp
var attributes = new AttributeSet();
var health = attributes.Define(Attribute.Health, 100f);
var moveSpeed = attributes.Define(Attribute.MoveSpeed, 5f);

health.ValueChanged += (oldValue, newValue) => Debug.Log($"Health: {oldValue} -> {newValue}");
```

Writing `BaseValue` directly is not allowed; use `AttributeSet.SetBaseValue(tag, value)` so the controller recalculates dependent attributes and raises `BaseValueChanged`.

### Creating Effects in Inspector

Create via `Assets > Create > GAS > Gameplay Effect`. The inspector shows duration policy, effect tag, modifier list, and tag conditions.

### Creating Effects in Code

```csharp
var def = new EffectDef(
    duration: DurationPolicy.Duration,
    durationSeconds: 5f,
    period: 1f,
    modifiers: new[] { new Modifier(Attribute.Health, ModifierOp.Add, -3f) },
    grantedTags: GameplayTagContainer.From(Status.Poisoned),
    applicationRequiredTags: new GameplayTagContainer(),
    applicationBlockedTags: GameplayTagContainer.From(Status.Immune),
    removeEffectsWithTags: new GameplayTagContainer(),
    effectTag: Effect.Poison,
    stacking: StackingPolicy.Replace
);

var spec = new EffectSpec(def, source: this, magnitude: 2f);
```

### Applying Effects

```csharp
// From ScriptableObject
var spec = _poisonEffect.CreateSpec(source: this, magnitude: 1f);
var handle = controller.ApplyEffect(spec);
// handle can be stored to remove the effect later
```

`ApplyEffect` returns `ActiveEffectHandle.Invalid` for instant effects (no tracking needed) or when application conditions fail.

### Ticking

```csharp
controller.Tick(Time.deltaTime);
```

Handles periodic modifier application (every `Period` seconds) and duration countdown with automatic removal of expired effects.

### Removing Effects

```csharp
// By handle (specific effect instance)
controller.RemoveEffect(handle);

// By tag (all effects matching the tag, uses hierarchy)
controller.RemoveEffectsWithTag(Effect.Poison);

// Everything
controller.RemoveAllEffects();
```

### Reacting to Changes

```csharp
// Attribute value changes (oldValue, newValue)
health.ValueChanged += (_, newValue) => _healthBar.SetValue(newValue);

// Effect lifecycle — both fire AFTER the list and tags are updated, attributes recalculated
controller.EffectApplied += effect => ShowBuffIcon(effect);
controller.EffectRemoved += effect => HideBuffIcon(effect);

// Query active effects
foreach (var effect in controller.ActiveEffects)
    Debug.Log($"{effect.Handle}: {effect.RemainingDuration}s remaining");
```

### Duration Policies

| Policy | Behavior |
|--------|----------|
| `Instant` | Modifies `BaseValue` immediately. No **ActiveEffect** created. |
| `Duration` | Persists for `DurationSeconds`, modifies `CurrentValue` via aggregation. |
| `Infinite` | Persists until manually removed. |

### Stacking Policies

| Policy | Behavior |
|--------|----------|
| `Independent` | Multiple instances of the same effect coexist. |
| `Replace` | New effect removes existing effects with the same `EffectTag`. |

### Modifier Aggregation

For persistent effects (`Duration` / `Infinite`):

```
result = hasOverride ? overrideValue : (BaseValue + addSum) * mulProduct
```

- `Add` — summed into `addSum`
- `Multiply` — multiplied into `mulProduct` (starts at 1)
- `Override` — wins over `Add`/`Multiply`. Across multiple `Override` modifiers, the one with the highest `Modifier.Priority` wins; ties resolve to the last applied. Useful for immunity overriding debuff, god-mode overriding anything, etc.

Instant effects modify `BaseValue` directly. Periodic modifiers on `Duration`/`Infinite` effects also apply to `BaseValue` each tick (like "damage over time permanently reduces the base") — model a separate `MaxHealth` attribute if you need a cap.

### Magnitude

`EffectSpec.Magnitude` multiplies the stored `Value` of every modifier in the effect. It scales the input to the aggregator, not the result:

- `Add 10` with `Magnitude 2` → contributes `+20` to `addSum`
- `Multiply 2` with `Magnitude 0.5` → contributes `*1.0` (effectively disables the multiplier)
- `Override 42` with `Magnitude 3` → overrides to `126`

For `Multiply`, think of magnitude as scaling the modifier's strength, not the final multiplier.

### Tag Conditions

| Field | Check |
|-------|-------|
| `ApplicationRequiredTags` | Target must have **all** these tags for the effect to apply. |
| `ApplicationBlockedTags` | Target must have **none** of these tags for the effect to apply. |
| `GrantedTags` | Tags added to the target while the effect is active. Removed when the effect ends. |
| `RemoveEffectsWithTags` | On application, removes all active effects whose `EffectTag` matches any of these tags. |

## Examples

### Instant Damage

```
Duration:   Instant
Modifiers:  [Health] [Add] [-25]
```

Subtracts 25 from `BaseValue` immediately. No tracking, no handle.

### Speed Buff (10 seconds)

```
Duration:   Duration, 10s
Effect Tag: Effect.SpeedBoost
Stacking:   Replace
Modifiers:  [MoveSpeed] [Multiply] [1.5]
```

Multiplies `CurrentValue` of MoveSpeed by 1.5 for 10 seconds. Replace stacking means reapplying refreshes the duration instead of stacking multiplicatively.

### Poison (DOT, 5 seconds, ticks every 1s)

```
Duration:     Duration, 5s
Period:       1s
Effect Tag:   Effect.Poison
Stacking:     Replace
Modifiers:    [Health] [Add] [-3]
Granted Tags: [Status.Poisoned]
Blocked Tags: [Status.Immune]
```

Every 1 second, subtracts 3 from Health `BaseValue`. Grants `Status.Poisoned` tag while active. Won't apply if the target has `Status.Immune`.

### Cleanse (removes debuffs on application)

```
Duration:              Instant
Remove Effects With:   [Effect.Debuff]
```

Removes all active effects tagged under `Effect.Debuff` (uses hierarchy -- removes `Effect.Debuff.Poison`, `Effect.Debuff.Slow`, etc).

### Stun Immunity Aura (infinite, blocks stuns)

```
Duration:     Infinite
Effect Tag:   Effect.StunImmunity
Granted Tags: [Status.Immune.Stun]
```

No modifiers -- only grants `Status.Immune.Stun` tag. Other effects that have `Status.Immune.Stun` in `ApplicationBlockedTags` will fail to apply. Stays until explicitly removed.

## Integration

GAS is framework-level -- it does not know about entities, components, or DI. Game code bridges effects to gameplay via ACS components:

```csharp
[ComponentDescription("Manages effect controller for the entity")]
public class EffectControllerComponent : EntityComponent
{
    private EffectsAspect _effects = default!;
    private EffectController _controller = default!;

    protected override void Awake()
    {
        base.Awake();
        _effects = Context.Require<EffectsAspect>();
        _controller = new EffectController(_effects.Attributes, _effects.Tags);
    }
}
```

## File Structure

```
com.rubickanov.gas/
├── Runtime/
│   ├── Attributes/
│   │   ├── GameplayAttribute.cs
│   │   └── AttributeSet.cs
│   ├── Effects/
│   │   ├── ModifierOp.cs
│   │   ├── Modifier.cs
│   │   ├── DurationPolicy.cs
│   │   ├── StackingPolicy.cs
│   │   ├── EffectDef.cs
│   │   ├── EffectSpec.cs
│   │   ├── ActiveEffect.cs
│   │   ├── ActiveEffectHandle.cs
│   │   └── EffectController.cs
│   └── Calculation/
│       └── ModifierAggregator.cs
├── Unity/
│   ├── GameplayEffectAsset.cs
│   └── SerializedModifier.cs
└── Editor/
    ├── GameplayEffectAssetEditor.cs
    └── SerializedModifierPropertyDrawer.cs
```
