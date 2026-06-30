# Gameplay Ability System (GAS)

Data-driven gameplay effects system. Attribute modifiers with duration, periodicity, stacking, and tag-based conditions.

## Dependencies

- `com.rubickanov.gameplaytags` — tags identify attributes, effects, and drive application/removal conditions

Unity 6000.0+.

## Architecture

```
GameplayEffectAsset (ScriptableObject)
        │  ToDef() / CreateSpec()
        ▼
    EffectDef (immutable definition)
        │  + source + magnitude
        ▼
    EffectSpec (runtime apply request)
        │  ApplyEffect()
        ▼
  EffectController ──► ActiveEffect (handle, duration & period timers)
        │                    │
        ▼                    ▼
  AttributeSet ◄── ModifierAggregator (recomputes CurrentValue)
```

`EffectController` owns the active effects, ticks their timers, and recomputes attribute values whenever the active set changes.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.GAS.Runtime** | No | Core logic — attributes, effects, aggregation. Pure C#. |
| **Rubickanov.GAS.Unity** | Yes | `GameplayEffectAsset`, `SerializedModifier` — inspector authoring. |
| **Rubickanov.GAS.Editor** | Editor | Custom inspector for effect assets, modifier property drawer. |

## Core Concepts

**GameplayAttribute** — Holds a `BaseValue` (authoritative) and a derived `CurrentValue`. The controller recomputes `CurrentValue` by aggregating active modifiers; `ValueChanged` fires `(oldValue, newValue)` on change. Mutate the base through `AttributeSet.SetBaseValue`, never by assignment.

**Modifier** — Readonly struct: target attribute tag, operation (`Add` / `Multiply` / `Override`), value, and an `Override` priority tie-breaker.

**EffectDef** — Immutable effect definition: duration policy, period, modifiers, tag conditions, stacking. Built from a **GameplayEffectAsset** via `ToDef()` or constructed in code.

**EffectSpec** — A request to apply an **EffectDef** with an optional source object and a magnitude scalar that scales every modifier's input value.

**EffectController** — Owns **ActiveEffect** instances, applies/removes/ticks them, and recalculates attributes on every change. Implements `IDisposable`.

## Quick Start

The `Attribute.*`, `Status.*`, and `Effect.*` identifiers below are project-defined `GameplayTag` constants from `com.rubickanov.gameplaytags`, not types this package exports:

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

2. Author a **GameplayEffectAsset** via `Assets > Create > GAS > Gameplay Effect`.

3. Apply effects and tick the controller:

```csharp
[SerializeField] private GameplayEffectAsset _poisonEffect = default!;

var spec = _poisonEffect.CreateSpec(source: this, magnitude: 1f);
ActiveEffectHandle handle = controller.ApplyEffect(spec);

// In your update loop
controller.Tick(Time.deltaTime);
```

## Usage

### Defining Attributes

`Define` registers an attribute and returns it (throws if the tag is already defined).

```csharp
var attributes = new AttributeSet();
GameplayAttribute health = attributes.Define(Attribute.Health, 100f);

health.ValueChanged += (oldValue, newValue) => Debug.Log($"Health: {oldValue} -> {newValue}");
```

Set base values through the set so the controller recomputes derived values:

```csharp
attributes.SetBaseValue(Attribute.Health, 80f); // fires BaseValueChanged → recalculation
```

`Get` returns the attribute or `null`; `TryGet` is the non-throwing variant.

### Building Effects in Code

Container arguments accept a `GameplayTagContainer` (implicitly converted), so use `GameplayTagContainer.From(...)` or `new GameplayTagContainer()`:

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
    stacking: StackingPolicy.Replace);

var spec = new EffectSpec(def, source: this, magnitude: 2f);
```

### Applying Effects

```csharp
var spec = _poisonEffect.CreateSpec(source: this, magnitude: 1f);
ActiveEffectHandle handle = controller.ApplyEffect(spec);

if (handle.IsValid)
    _activeBuffs.Add(handle); // store to remove this instance later
```

`ApplyEffect` returns `ActiveEffectHandle.Invalid` for `Instant` effects (nothing to track) and when application conditions fail.

### Ticking

```csharp
controller.Tick(Time.deltaTime);
```

Advances period accumulators (firing periodic modifiers each `Period` seconds), counts down `Duration` effects, and removes expired ones.

### Removing Effects

Each removal method returns the number of effects removed.

```csharp
controller.RemoveEffect(handle);            // one specific instance
controller.RemoveEffectsWithTag(Effect.Poison); // all whose EffectTag matches (hierarchical)
controller.RemoveAllEffects();
```

### Reacting to Changes

```csharp
health.ValueChanged += (_, newValue) => _healthBar.SetValue(newValue);

// Lifecycle events fire AFTER the active set, granted tags, and attributes are updated.
// Not fired for Instant effects.
controller.EffectApplied += effect => ShowBuffIcon(effect);
controller.EffectRemoved += effect => HideBuffIcon(effect);

foreach (ActiveEffect effect in controller.ActiveEffects)
    Debug.Log($"{effect.Handle}: {effect.RemainingDuration}s remaining");
```

Both lifecycle events and `ValueChanged` are reentrancy-safe: a handler may call `ApplyEffect` / `RemoveEffect` and those take effect immediately.

### Lifecycle

`EffectController` subscribes to `AttributeSet.BaseValueChanged`. Dispose it when the controller is discarded but the attribute set lives on (pooling, respawn, re-init) to detach the handler:

```csharp
controller.Dispose(); // idempotent
```

### Duration Policies

| Policy | Behavior |
|--------|----------|
| `Instant` | Modifies `BaseValue` once. No **ActiveEffect** created, no handle. |
| `Duration` | Persists for `DurationSeconds`, modifies `CurrentValue` via aggregation. |
| `Infinite` | Persists until manually removed, modifies `CurrentValue` via aggregation. |

### Stacking Policies

| Policy | Behavior |
|--------|----------|
| `Independent` | Multiple instances of the same effect coexist. |
| `Replace` | Applying a new instance removes existing effects with the same `EffectTag`. |

### Modifier Aggregation

For persistent (`Duration` / `Infinite`) effects, `CurrentValue` is:

```text
result = hasOverride ? overrideValue : (BaseValue + addSum) * mulProduct
```

- `Add` — summed into `addSum`.
- `Multiply` — multiplied into `mulProduct` (starts at 1).
- `Override` — wins over `Add` / `Multiply`. Across multiple overrides the highest `Modifier.Priority` wins; ties resolve to the last applied.

`Instant` effects and periodic ticks write `BaseValue` directly (a DOT permanently lowers the base — model a separate `MaxHealth` attribute if you need a cap). Periodic effects therefore contribute through `BaseValue` only and are excluded from the continuous aggregate to avoid double-counting.

### Magnitude

`EffectSpec.Magnitude` scales the input value of every modifier, not the result:

- `Add 10` with magnitude 2 → contributes `+20` to `addSum`.
- `Multiply 2` with magnitude 0.5 → contributes `*1.0` (effectively disables the multiplier).
- `Override 42` with magnitude 3 → overrides to `126`.

### Tag Conditions

| Field | Check |
|-------|-------|
| `ApplicationRequiredTags` | Target must have **all** of these for the effect to apply. |
| `ApplicationBlockedTags` | Target must have **none** of these for the effect to apply. |
| `GrantedTags` | Added to the target's tag container while active; removed when the effect ends (unless another active effect still grants them). |
| `RemoveEffectsWithTags` | On application, removes active effects whose `EffectTag` matches any of these (hierarchical). |

## Examples

### Instant Damage

```text
Duration:   Instant
Modifiers:  [Health] [Add] [-25]
```

Subtracts 25 from `BaseValue` immediately. No handle, no tracking.

### Speed Buff, 10 Seconds

```text
Duration:   Duration, 10s
Effect Tag: Effect.SpeedBoost
Stacking:   Replace
Modifiers:  [MoveSpeed] [Multiply] [1.5]
```

Multiplies MoveSpeed `CurrentValue` by 1.5 for 10 seconds. `Replace` means reapplying drops the old instance and starts a fresh duration instead of stacking multiplicatively.

### Poison DOT, 5 Seconds, Ticks Every 1s

```text
Duration:     Duration, 5s
Period:       1s
Effect Tag:   Effect.Poison
Stacking:     Replace
Modifiers:    [Health] [Add] [-3]
Granted Tags: [Status.Poisoned]
Blocked Tags: [Status.Immune]
```

Subtracts 3 from Health `BaseValue` each second, grants `Status.Poisoned` while active, and refuses to apply if the target has `Status.Immune`.

### Cleanse

```text
Duration:            Instant
Remove Effects With: [Effect.Debuff]
```

Removes all active effects tagged under `Effect.Debuff` (`Effect.Debuff.Poison`, `Effect.Debuff.Slow`, ...).

### Stun Immunity Aura

```text
Duration:     Infinite
Effect Tag:   Effect.StunImmunity
Granted Tags: [Status.Immune.Stun]
```

No modifiers — only grants `Status.Immune.Stun`. Stun effects that list it in `ApplicationBlockedTags` fail to apply. Stays until explicitly removed.

## Integration

GAS is framework-level — it knows nothing about entities, components, or DI. Game code bridges a controller to a target's attributes and tags:

```csharp
[ComponentDescription("Owns the effect controller for the entity")]
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

    private void Update() => _controller.Tick(Time.deltaTime);

    protected override void OnDestroy()
    {
        _controller.Dispose();
        base.OnDestroy();
    }
}
```

## File Structure

```text
com.rubickanov.gas/
├── Runtime/
│   ├── Attributes/        — GameplayAttribute, AttributeSet
│   ├── Effects/           — Modifier, EffectDef, EffectSpec, ActiveEffect, EffectController, policies
│   └── Calculation/       — ModifierAggregator
├── Unity/                 — GameplayEffectAsset, SerializedModifier
└── Editor/                — asset inspector, modifier drawer
```
