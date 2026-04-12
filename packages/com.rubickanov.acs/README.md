# Aspect-Component System (ACS)

Entity composition framework for Unity. Aspects hold reactive data, components drive behavior, EntityContext ties them together.

## Dependencies

- `R3` — reactive primitives (`ReactiveProperty<T>`, `Subject<T>`, `DisposableBag`)

## Architecture

```
IEntityAspect (marker interface)
    ^
    |  Require<T>() / [Aspect] injection
EntityContext (per-entity aspect registry)
    ^
    |  Context property
EntityComponent (MonoBehaviour base, OnSubscribe hook)


SingletonEntityContext<T>   →   World (scene-wide singleton + entity registry)
    ↓                               ↓
EntityContext              EntityQuery<T1..T8> (find entities by aspect types)
```

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **ACS.Runtime** | Yes | Core framework: EntityContext, EntityComponent, World, EntityQuery |
| **ACS.Editor** | Editor | EntityContext inspector with aspect usage analysis |

## Core Concepts

**Aspect** — Pure data container that implements **IEntityAspect**. Only holds reactive fields and event signals. Zero logic, zero methods.

**Component** — Single unit of behavior that reads and writes aspects. Extends **EntityComponent** (MonoBehaviour). One component, one job.

**EntityContext** — MonoBehaviour on the entity root that serves as the shared aspect registry. Components obtain aspects via `[Aspect]` field injection or `Context.Require<T>()`.

**World** — Singleton **EntityContext** that also tracks every other context in the scene. Hosts world-scoped aspects (time of day, weather) accessed via `World.Require<T>()`, and answers aspect-type queries via `World.Query<T>()`.

## Quick Start

1. Add **EntityContext** to the root GameObject of your entity.
2. Define an aspect:

```csharp
public class HealthAspect : IEntityAspect
{
    public readonly ReactiveProperty<int> CurrentHealth = new(100);
    public readonly ReactiveProperty<bool> IsAlive = new(true);
    public readonly Subject<DamageInfo> Hit = new();
}
```

3. Write a component that reacts to it:

```csharp
public class DamageFlashObserver : EntityComponent
{
    [Aspect] private HealthAspect _health = default!;

    protected override void OnSubscribe(ref DisposableBag disposables)
    {
        _health.Hit.Subscribe(OnHit).AddTo(ref disposables);
    }

    private void OnHit(DamageInfo info) { /* flash logic */ }
}
```

The `[Aspect]` attribute resolves the field from the context in `Awake`. `OnSubscribe` wires subscriptions that are automatically disposed on `OnDisable`.

## Usage

### Defining Aspects

Aspects are pure data — only `ReactiveProperty<T>` fields and `Subject<T>` events. No methods, no constructors with arguments:

```csharp
public class MovementAspect : IEntityAspect
{
    public readonly ReactiveProperty<Vector3> Velocity = new(Vector3.zero);
    public readonly ReactiveProperty<float> MoveSpeed = new(5f);
    public readonly ReactiveProperty<bool> IsGrounded = new(true);
}
```

### Injecting Aspects into Components

Mark fields with `[Aspect]` — injection happens in `EntityComponent.Awake` via reflection (cached per component type). Multiple components requesting the same type share the same instance:

```csharp
public class CharacterController : EntityComponent
{
    [Aspect] private MovementAspect _movement = default!;
    [Aspect] private HealthAspect _health = default!;

    private void Update()
    {
        if (_health.IsAlive.Value)
            transform.position += _movement.Velocity.Value * Time.deltaTime;
    }
}
```

For non-component consumers (plain C# classes) call the injector directly:

```csharp
AspectInjector.Inject(context, myPlainClassInstance);
```

### Component Lifecycle

Override `OnSubscribe` — the base class hands you a `DisposableBag` that is disposed on `OnDisable` and re-populated on the next `OnEnable`:

```csharp
protected override void OnSubscribe(ref DisposableBag disposables)
{
    _health.CurrentHealth.Subscribe(OnHealthChanged).AddTo(ref disposables);
    _movement.Velocity.Subscribe(OnVelocityChanged).AddTo(ref disposables);
}
```

If you override `Awake`, always call `base.Awake()` — that is what triggers aspect injection.

### Optional Aspects

Use `TryGet<T>()` when an aspect may or may not be present, and `Has<T>()` for plain existence checks:

```csharp
if (Context.TryGet<ShieldAspect>(out var shield))
    shield.CurrentShield.Value -= overflow;

if (Context.Has<FlyingAspect>())
    ApplyGravity = false;
```

`[Aspect]` injection is not optional — missing types will throw because `Require<T>` creates the aspect if absent. Use `TryGet<T>` for the opt-in case.

### World — Global State and Queries

Add a **World** MonoBehaviour to a GameObject in your scene. It is itself an `EntityContext`, so world-scoped aspects work exactly like entity aspects:

```csharp
public class TimeAspect : IEntityAspect
{
    public readonly ReactiveProperty<float> TimeOfDay = new(0f);
    public readonly ReactiveProperty<int> Day = new(1);
    public readonly Subject<Unit> DayStarted = new();
}

// Read / write world state from anywhere
var time = World.Require<TimeAspect>().TimeOfDay.Value;
```

Components can also inject world aspects via a static accessor inside `OnSubscribe`:

```csharp
public class ZombieBehavior : EntityComponent
{
    [Aspect] private MovementAspect _movement = default!;

    protected override void OnSubscribe(ref DisposableBag disposables)
    {
        World.Require<TimeAspect>().TimeOfDay
            .Subscribe(t => _movement.MoveSpeed.Value = t > 20f ? 5f : 2f)
            .AddTo(ref disposables);
    }
}
```

`World.Query<T>()` returns every aspect of that type currently in the scene. Multi-argument overloads (up to 8) yield tuples of `(entity, aspect1, aspect2, ...)`:

```csharp
// All living health aspects
var alive = World.Query<HealthAspect>()
    .Where(h => h.CurrentHealth.Value > 0);

// Entities carrying both health and position
foreach (var (entity, health, pos) in World.Query<HealthAspect, PositionAspect>())
    Debug.Log($"{entity.name} @ {pos.Value.Value}, hp={health.CurrentHealth.Value}");
```

Registration is automatic — every `Require<T>` call on any `EntityContext` registers with the live `World`. Destruction unregisters.

### Custom Singletons

`SingletonEntityContext<T>` is the base for any scene-wide singleton context. Subclass it when you need a second global entity (a `GameSession`, a `MatchDirector`, a `DialogueRuntime`):

```csharp
public class MatchDirector : SingletonEntityContext<MatchDirector>
{
    // Add [Aspect] fields and methods as usual.
}

// Access anywhere:
MatchDirector.Instance?.Require<ScoreAspect>();
```

Duplicate instances self-destruct their GameObject during `Awake`. `Instance` is cleared on destroy.

### Dependency Injection

ACS does not depend on any DI framework. **EntityInjector** is a static delegate that components invoke before `[Aspect]` injection — wire it once from your container:

```csharp
EntityInjector.Inject = go =>
{
    var scope = LifetimeScope.Find<LifetimeScope>(go.scene);
    scope?.Container.InjectGameObject(go);
};
```

### Extension Hook

`EntityContext.OnContextInitialized` fires once in `Start` after every component's `Awake` has run and aspects have been created. Extension packages (ACS.Netcode, future ACS.Persistence) subscribe here for auto-setup:

```csharp
EntityContext.OnContextInitialized += context =>
{
    if (context.Has<HealthAspect>())
        AutoWireReplication(context);
};
```

## Integration

Assemble entity behavior by adding components to a GameObject hierarchy. Components discover each other through shared aspects — never through direct references:

```
Character (EntityContext)
├── CharacterMovement      — writes MovementAspect.Velocity
├── PlayerMovementInput    — writes MovementAspect from player input
├── CharacterAnimator      — reads MovementAspect, writes AnimationAspect
└── DamageFlashObserver    — reads HealthAspect.Hit

World (SingletonEntityContext<World>)
├── TimeAspect             — global time of day
└── WeatherAspect          — current weather
```

## Design Decisions

- **EntityContext lazy-creates aspects** — `Require<T>()` returns an existing instance or creates a new one. No manual registration, no initialization order coupling.
- **`[Aspect]` injection over `Require<T>` in Awake** — eliminates boilerplate, keeps the Awake override optional, and makes aspect dependencies a declarative part of the component's shape.
- **`OnSubscribe` hook instead of manual `OnEnable`/`OnDisable`** — guarantees subscriptions are always paired with disposal. Component authors cannot forget to dispose.
- **World is just an EntityContext** — replication (`[ReplicatedState]`), persistence, and inspector tooling all work on world aspects for free. No separate "world component" abstraction.
- **Queries ship in core, spatial queries do not** — `World.Query<T>()` is a type-bucket lookup with no physics dependency. Spatial queries (radius, nearest, grid) live in a separate package to keep core dependency-free.
- **Static EntityInjector delegate instead of DI dependency** — keeps the package zero-DI. Any framework (VContainer, Zenject) plugs in with one line.
- **No EntityNetworkComponent in this package** — netcode support lives in the ACS.Netcode extension to avoid a hard dependency on Netcode for GameObjects.
