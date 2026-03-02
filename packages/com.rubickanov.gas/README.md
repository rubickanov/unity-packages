# Gameplay Ability System (GAS)

Data-driven gameplay effects system. Attribute modifiers with duration, periodicity, stacking, and tag-based conditions.

Inspired by Unreal Engine's GAS, but minimal and focused — effects and attributes only, no abilities or cooldowns.

## Dependencies

- `com.rubickanov.gameplaytags` — tag-based attribute identification and effect conditions

## Architecture

```
GameplayEffectAsset (ScriptableObject, designer-facing)
        │
        ▼
    EffectDef (immutable definition)
        │
        ▼
    EffectSpec (runtime instance: def + source + magnitude)
        │
        ▼
  EffectController ──▶ ActiveEffect (tracked handle, timers)
        │                    │
        ▼                    ▼
  AttributeSet ◀── ModifierAggregator (recalculates CurrentValue)
        │
        ▼
  GameplayAttribute (BaseValue → CurrentValue + ValueChanged event)
```

### Assemblies

| Assembly | Engine refs | Description |
|---|---|---|
| **GAS.Runtime** | No | Core logic — attributes, effects, aggregation. Pure C#. |
| **GAS.Unity** | Yes | `GameplayEffectAsset`, `SerializedModifier` — inspector integration. |
| **GAS.Editor** | Editor | Custom inspector for effect assets, modifier property drawer. |

## Core Concepts

### Attributes

`GameplayAttribute` holds a `BaseValue` and a computed `CurrentValue`. The controller recalculates `CurrentValue` whenever active effects change.

```csharp
var attributes = new AttributeSet();
var health = attributes.Define(Attribute.Health, 100f);
var moveSpeed = attributes.Define(Attribute.MoveSpeed, 5f);

health.ValueChanged += value => Debug.Log($"Health: {value}");
```

### Modifiers

A modifier describes a single operation on a single attribute:

```csharp
new Modifier(Attribute.Health, ModifierOp.Add, -10f)    // deal 10 damage
new Modifier(Attribute.MoveSpeed, ModifierOp.Multiply, 0.5f) // halve speed
new Modifier(Attribute.MoveSpeed, ModifierOp.Override, 0f)    // root (force zero)
```

**Aggregation formula** for persistent effects (Duration / Infinite):

```
result = hasOverride ? overrideValue : (BaseValue + addSum) * mulProduct
```

- `Add` — summed into `addSum`
- `Multiply` — multiplied into `mulProduct` (starts at 1)
- `Override` — wins over everything

**Instant** effects modify `BaseValue` directly and are not tracked.

### Duration Policies

| Policy | Behavior |
|---|---|
| `Instant` | Modifies `BaseValue` immediately. No `ActiveEffect` created. |
| `Duration` | Persists for `DurationSeconds`, modifies `CurrentValue` via aggregation. |
| `Infinite` | Persists until manually removed. |

### Stacking Policies

| Policy | Behavior |
|---|---|
| `Independent` | Multiple instances of the same effect coexist. |
| `Replace` | New effect removes existing effects with the same `EffectTag`. |

### Periodic Effects

Effects with `Period > 0` apply their modifiers as instant (to `BaseValue`) every `Period` seconds. Useful for damage-over-time, health regeneration, etc.

### Tag Conditions

Effects support four tag-based conditions, all using hierarchical matching:

| Field | Check |
|---|---|
| `ApplicationRequiredTags` | Target must have **all** these tags for the effect to apply. |
| `ApplicationBlockedTags` | Target must have **none** of these tags for the effect to apply. |
| `GrantedTags` | Tags added to the target while the effect is active. Removed when the effect ends (unless another active effect grants the same tag). |
| `RemoveEffectsWithTags` | On application, removes all active effects whose `EffectTag` matches any of these tags. |

## Usage

### Setup

```csharp
// Define attributes
var attributes = new AttributeSet();
attributes.Define(Attribute.Health, 100f);
attributes.Define(Attribute.MoveSpeed, 5f);

// Tag container for the entity (shared with other systems)
var tags = new GameplayTagContainer();

// Create controller
var controller = new EffectController(attributes, tags);
```

### Creating Effects in Inspector

Create via `Assets > Create > GAS > Gameplay Effect`. The inspector shows:

- **Duration** — policy + seconds (if Duration) + period + stacking (if not Instant)
- **Effect Tag** — identifier for stacking and tag-based removal
- **Modifiers** — list of `[Attribute] [Operation] [Value]` rows
- **Tags** — granted, required, blocked, remove-with

### Applying Effects

```csharp
// From ScriptableObject (typical)
[SerializeField] private GameplayEffectAsset _poisonEffect = default!;

void ApplyPoison(object source)
{
    var spec = _poisonEffect.CreateSpec(source: source, magnitude: 1f);
    var handle = _controller.ApplyEffect(spec);
    // handle can be stored to remove the effect later
}

// From code (when needed)
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

var spec = new EffectSpec(def, source: this, magnitude: 2f); // magnitude scales all modifier values
var handle = _controller.ApplyEffect(spec);
```

### Ticking

```csharp
void Update()
{
    _controller.Tick(Time.deltaTime);
}
```

`Tick` handles:
- Periodic modifier application (every `Period` seconds)
- Duration countdown and automatic removal of expired effects

### Removing Effects

```csharp
// By handle (specific effect instance)
_controller.RemoveEffect(handle);

// By tag (all effects matching the tag, uses hierarchy)
_controller.RemoveEffectsWithTag(Effect.Poison);

// Everything
_controller.RemoveAllEffects();
```

### Reacting to Changes

```csharp
// Attribute value changes
health.ValueChanged += value => _healthBar.SetValue(value);

// Effect lifecycle
_controller.EffectApplied += effect => ShowBuffIcon(effect);
_controller.EffectRemoved += effect => HideBuffIcon(effect);

// Query active effects
foreach (var effect in _controller.ActiveEffects)
    Debug.Log($"{effect.Handle}: {effect.RemainingDuration}s remaining");
```

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

Every 1 second, subtracts 3 from Health `BaseValue`. Grants `Status.Poisoned` tag while active (for visual feedback, AI checks, etc). Won't apply if the target has `Status.Immune`.

### Cleanse (removes debuffs on application)

```
Duration:              Instant
Remove Effects With:   [Effect.Debuff]
```

Instantly removes all active effects tagged under `Effect.Debuff` (uses hierarchy — removes `Effect.Debuff.Poison`, `Effect.Debuff.Slow`, etc).

### Stun Immunity Aura (infinite, blocks stuns)

```
Duration:     Infinite
Effect Tag:   Effect.StunImmunity
Granted Tags: [Status.Immune.Stun]
```

No modifiers — only grants `Status.Immune.Stun` tag. Other effects that have `Status.Immune.Stun` in `ApplicationBlockedTags` will fail to apply. Stays until explicitly removed.

## Integration with Game Code

GAS is framework-level — it doesn't know about entities, components, or DI. Game code bridges effects to gameplay via components:

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
├── package.json
├── README.md
├── Runtime/
│   ├── Attributes/
│   │   ├── GameplayAttribute.cs    # BaseValue / CurrentValue / ValueChanged
│   │   └── AttributeSet.cs         # Dictionary<GameplayTag, GameplayAttribute>
│   ├── Effects/
│   │   ├── ModifierOp.cs           # Add, Multiply, Override
│   │   ├── Modifier.cs             # readonly struct (attribute + op + value)
│   │   ├── DurationPolicy.cs       # Instant, Duration, Infinite
│   │   ├── StackingPolicy.cs       # Independent, Replace
│   │   ├── EffectDef.cs            # Immutable effect definition
│   │   ├── EffectSpec.cs           # Def + Source + Magnitude
│   │   ├── ActiveEffect.cs         # Runtime state (handle, timers)
│   │   ├── ActiveEffectHandle.cs   # Opaque handle for removal
│   │   └── EffectController.cs     # Apply / Remove / Tick / Recalculate
│   └── Calculation/
│       └── ModifierAggregator.cs   # Aggregation formula + instant application
├── Unity/
│   ├── GameplayEffectAsset.cs      # ScriptableObject (ToDef / CreateSpec)
│   └── SerializedModifier.cs       # Serializable modifier for inspector
└── Editor/
    ├── GameplayEffectAssetEditor.cs         # Custom inspector layout
    └── SerializedModifierPropertyDrawer.cs  # Inline [Tag] [Op] [Value] drawer
```
