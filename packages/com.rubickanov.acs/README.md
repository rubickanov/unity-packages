# Aspect-Component System (ACS)

Entity composition framework for Unity. Aspects hold reactive data, components drive behavior, MonoEntity ties them together.

## Dependencies

- `R3` — reactive primitives (`ReactiveProperty<T>`, `Subject<T>`, `DisposableBag`)
- `ObservableCollections` + `ObservableCollections.R3` — reactive collections for aspect fields (`ObservableList<T>`, `ObservableDictionary<TKey,TValue>`, `ObservableHashSet<T>`, `ObservableRingBuffer<T>`)

## Architecture

```
IEntityAspect (marker interface)
    ^
    |  Require<T>() / [Aspect] injection
IEntity ── Entity (pure POCO)
    ^  ── MonoEntity (MonoBehaviour adapter)
    |
    |  Context property
EntityComponent (MonoBehaviour base, OnSubscribe hook)
IEntityLogic    (pure reactive, AttachLogic auto-dispose)
ITickable       (pure update loop, driven by EntityTickRunner)


SingletonMonoEntity<T>   →   MonoWorld (scene-wide singleton)
    ↓                               ↓
MonoEntity               World (pure registry + EntityQuery<T1..T8>)
```

Three tiers of entity behavior:

1. **Reactive-only** (~80%) — `IEntityLogic` attached via `entity.AttachLogic(...)`. One plain C# class, auto-disposed when the entity is destroyed.
2. **Tickable** (~15%) — `ITickable` driven by `EntityTickRunner` (Unity) or a headless loop (console host, fixed-step server). Same class, different frame source.
3. **Unity-bound** (~5%) — `EntityComponent : MonoBehaviour` for behaviour that genuinely needs `Transform`, `Rigidbody`, `Animator`, `Canvas`, etc.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **ACS.Runtime** | Yes | Core framework: MonoEntity, EntityComponent, World, MonoWorld, EntityQuery |
| **ACS.Editor** | Editor | MonoEntity inspector with aspect usage analysis |

## Core Concepts

**Aspect** — Pure data container that implements **IEntityAspect**. Only holds reactive fields and event signals. Zero logic, zero methods.

**Component** — Single unit of behavior that reads and writes aspects. Extends **EntityComponent** (MonoBehaviour). One component, one job.

**IEntity** — The aspect-container contract (`Require<T>` / `TryGet<T>` / `Has<T>` / `GetAllAspects` / `Destroyed`). Implemented by the Unity-bound `MonoEntity` and the pure POCO `Entity`. Anything that only needs to read/write aspects should depend on `IEntity` so it can run without Unity.

**MonoEntity** — MonoBehaviour on the entity root. Components obtain aspects via `[Aspect]` field injection or `Context.Require<T>()`. Fires `Destroyed` from `OnDestroy`.

**Entity** — Pure C# `IEntity` for pocket entities (item in an inventory, buff without a visual), headless simulations, and edit-mode tests. Lifetime ends with `Dispose()`, which fires `Destroyed` and clears the aspect dictionary.

**World** — Pure-C# `IEntity` that owns world-scoped aspects and the entity registry + query surface. Construct one directly for headless simulations. Exposes a static `Current` slot (one active world at a time) that backs `World.Require<T>()` and `World.Query<T>()` — both throw `InvalidOperationException` if no world is active, so call sites stay honest. No Unity dependencies.

**MonoWorld** — `SingletonMonoEntity<MonoWorld>` that owns a `World` instance, assigns it as `World.Current` during Awake, and clears it during OnDestroy. Drop one on a GameObject in the scene and every `MonoEntity` in that scene auto-registers with `MonoWorld.Instance.World`. All IEntity calls on the MonoWorld delegate into the embedded `World` — there is no duplicate aspect store.

## Quick Start

1. Add **MonoEntity** to the root GameObject of your entity.
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

### Reactive Collections

`ReactiveProperty<T[]>` or `ReactiveProperty<List<T>>` does not scale — every mutation replaces the whole container and re-triggers subscribers for items that never changed. Use `ObservableCollections` types directly as aspect fields when a collection is part of the data shape (inventory, buffs, cooldowns, tag sets, damage log):

```csharp
public class InventoryAspect : IEntityAspect
{
    public readonly ObservableList<ItemStack> Items = new();
    public readonly ObservableDictionary<SkillId, float> Cooldowns = new();
    public readonly ObservableHashSet<GameplayTag> Tags = new();
    public readonly ObservableFixedSizeRingBuffer<DamageEvent> DamageLog = new(capacity: 16);
}
```

Subscribe to fine-grained deltas via the R3 bridge — `ObserveAdd` / `ObserveRemove` / `ObserveReplace` / `ObserveMove` / `ObserveReset` / `ObserveCountChanged`:

```csharp
protected override void OnSubscribe(ref DisposableBag disposables)
{
    _inventory.Items.ObserveAdd().Subscribe(e => _ui.AddSlot(e.Index, e.Value)).AddTo(ref disposables);
    _inventory.Items.ObserveRemove().Subscribe(e => _ui.RemoveSlot(e.Index)).AddTo(ref disposables);
    _inventory.Cooldowns.ObserveReplace().Subscribe(e => _ui.SetCooldown(e.NewValue.Key, e.NewValue.Value)).AddTo(ref disposables);
}
```

`ACS.Netcode` does not yet delta-replicate `IObservableCollection<T>` fields — marking one `[Replicated]` produces a targeted error. For networked collections, subscribe to mutations locally and relay them via a custom RPC until native support lands.

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

If you override `Awake` in an `EntityComponent`, always call `base.Awake()` — that is what runs `[Aspect]` injection:

```csharp
protected override void Awake()
{
    base.Awake(); // injects [Aspect] fields
    // your init
}
```

The same rule applies to `SingletonMonoEntity<T>` subclasses (including `MonoWorld`), but for a different reason: `base.Awake()` assigns the static `Instance`. Skip it and `MonoWorld.Instance` / `YourSingleton.Instance` stay `null`:

```csharp
public class MyWorld : SingletonMonoEntity<MyWorld>
{
    protected override void Awake()
    {
        base.Awake(); // sets Instance = this
        // your init
    }
}
```

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

Add a **World** MonoBehaviour to a GameObject in your scene. It is itself an `MonoEntity`, so world-scoped aspects work exactly like entity aspects:

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
// All living health aspects — plain foreach keeps the query's struct enumerator zero-alloc.
foreach (var health in World.Query<HealthAspect>())
{
    if (health.CurrentHealth.Value <= 0) continue;
    // ...
}

// Entities carrying both health and position
foreach (var (entity, health, pos) in World.Query<HealthAspect, PositionAspect>())
    Debug.Log($"{entity.name} @ {pos.Value.Value}, hp={health.CurrentHealth.Value}");
```

Registration is automatic — every `Require<T>` call on any `MonoEntity` registers with the live `World`. Destruction unregisters.

### Custom Singletons

`SingletonMonoEntity<T>` is the base for any scene-wide singleton context. Subclass it when you need a second global entity (a `GameSession`, a `MatchDirector`, a `DialogueRuntime`):

```csharp
public class MatchDirector : SingletonMonoEntity<MatchDirector>
{
    // Add [Aspect] fields and methods as usual.
}

// Access anywhere:
MatchDirector.Instance?.Require<ScoreAspect>();
```

Duplicate instances self-destruct their GameObject during `Awake`. `Instance` is cleared on destroy.

### Dependency Injection

ACS does not depend on any DI framework. **EntityInjector** is a static hook that components invoke before `[Aspect]` injection — wire it once from your container:

```csharp
EntityInjector.SetInjector(go =>
{
    var scope = LifetimeScope.Find<LifetimeScope>(go.scene);
    scope?.Container.InjectGameObject(go);
});
```

Call `EntityInjector.ClearInjector()` to reset (tests, teardown). Replacing the injector with a different delegate logs a warning — usually a sign that two DI containers are competing.

### Pure Core — Entity, IEntityLogic, ITickable

The Unity-bound `MonoEntity` is one of two `IEntity` implementations. The other — `Entity` — is plain C#, suitable for pocket entities, headless simulations, and edit-mode tests that don't want to boot the player loop.

```csharp
var entity = new Entity();
var health = entity.Require<HealthAspect>();
entity.AttachLogic(new DeathWatchLogic(health));

// Later, when the entity should stop existing:
entity.Dispose();  // fires Destroyed; AttachLogic disposes DeathWatchLogic.
```

**IEntityLogic** is the pure-C# equivalent of `EntityComponent`: wire subscriptions in the constructor, release them in `Dispose`. `AttachLogic(entity, logic)` hooks `Destroyed` for you, so the logic disposes automatically when the owning entity dies.

```csharp
public sealed class DeathWatchLogic : IEntityLogic
{
    private readonly IDisposable _sub;
    public DeathWatchLogic(HealthAspect health)
        => _sub = health.Current.Subscribe(v => { if (v <= 0) OnDied(); });
    public void Dispose() => _sub.Dispose();
    private void OnDied() { /* ... */ }
}
```

**ITickable** is the per-frame/per-step contract. Add an `EntityTickRunner` next to the `MonoEntity` in Unity, or drive `Tick(dt)` from your own loop in a headless build — the logic class doesn't know the difference:

```csharp
public sealed class CooldownTickable : ITickable
{
    private readonly WeaponAspect _weapon;
    public CooldownTickable(WeaponAspect weapon) => _weapon = weapon;
    public void Tick(float dt) => _weapon.Cooldown.Value = Mathf.Max(0, _weapon.Cooldown.Value - dt);
}
```

For scene-wide queries without a `MonoWorld` in the scene, construct a pure `World` directly and pass it to your entities — they auto-register on each first `Require<T>()` and auto-unregister on `Dispose`, the same way `MonoEntity` integrates with `World.Current`:

```csharp
var world = new World();

var hero = new Entity(world);
hero.Require<HealthAspect>();
hero.Require<PositionAspect>();

foreach (var (owner, health, pos) in world.QueryLocal<HealthAspect, PositionAspect>())
    // ...

hero.Dispose(); // auto-unregisters — no manual world.Unregister needed.
```

If you want finer control (pocket entities that join a registry only conditionally, or headless sims that own registration externally), use the parameterless `new Entity()` ctor and call `world.Register(entity, typeof(T))` / `world.Unregister(entity, entity.AspectTypes)` yourself.

### Extension Hooks

Two static events expose entity lifecycle to extension packages (ACS.Netcode, future ACS.Persistence). Both are reset at the start of every play session, so subscribers registered via `InitializeOnLoad` don't leak between sessions when Domain Reload is disabled.

`MonoEntity.OnAwakeCompleted` fires once per entity in `Start`, after every component's `Awake` has run. Use it for per-entity initial setup — aspects created during Awake-time injection are guaranteed to be present:

```csharp
MonoEntity.OnAwakeCompleted += context =>
{
    if (context.Has<HealthAspect>())
        AutoWireReplication(context);
};
```

`MonoEntity.OnAspectCreated` fires once for every new aspect, including those created lazily via `Require` after `Start` (e.g. from `OnEnable`, `Update`, or delayed logic). Use it to react to aspects that may appear at any time during an entity's life:

```csharp
MonoEntity.OnAspectCreated += (entity, aspectType) =>
{
    if (aspectType == typeof(HealthAspect))
        WireHealthReplication(entity);
};
```

## Integration

Assemble entity behavior by adding components to a GameObject hierarchy. Components discover each other through shared aspects — never through direct references:

```
Character (MonoEntity)
├── CharacterMovement      — writes MovementAspect.Velocity
├── PlayerMovementInput    — writes MovementAspect from player input
├── CharacterAnimator      — reads MovementAspect, writes AnimationAspect
└── DamageFlashObserver    — reads HealthAspect.Hit

World (SingletonMonoEntity<World>)
├── TimeAspect             — global time of day
└── WeatherAspect          — current weather
```

## Design Decisions

- **MonoEntity lazy-creates aspects** — `Require<T>()` returns an existing instance or creates a new one. No manual registration, no initialization order coupling.
- **`[Aspect]` injection over `Require<T>` in Awake** — eliminates boilerplate, keeps the Awake override optional, and makes aspect dependencies a declarative part of the component's shape.
- **`OnSubscribe` hook instead of manual `OnEnable`/`OnDisable`** — guarantees subscriptions are always paired with disposal. Component authors cannot forget to dispose.
- **MonoWorld is just a MonoEntity** — replication (`[Replicated]`), persistence, and inspector tooling all work on world aspects for free. No separate "world component" abstraction.
- **Queries ship in core, spatial queries do not** — `World.Query<T>()` is a type-bucket lookup with no physics dependency. Spatial queries (radius, nearest, grid) live in a separate package to keep core dependency-free.
- **Static EntityInjector delegate instead of DI dependency** — keeps the package zero-DI. Any framework (VContainer, Zenject) plugs in with one line.
- **No EntityNetworkComponent in this package** — netcode support lives in the ACS.Netcode extension to avoid a hard dependency on Netcode for GameObjects.
- **Pure core split** — identity/composition (aspects, registry, queries) is plain C# via `IEntity` + `Entity` + `World`. `MonoEntity` / `MonoWorld` are the Unity adapters. Pocket entities, console-host simulations, and fast edit-mode tests never touch `MonoBehaviour`. The Unity-bound tier (`EntityComponent`, `EntityTickRunner`) exists only where `Transform`/`Rigidbody`/`Animator` actually matter.

## Migration

**`EntityContext` → `MonoEntity`** (rename, 2026-04): the MonoBehaviour class is now called `MonoEntity` to symmetrically pair with the new pure `Entity`. `[MovedFrom]` preserves serialized prefab/scene references, but update type names in code:

| Before                   | After                |
|--------------------------|----------------------|
| `EntityContext`          | `MonoEntity`         |
| `SingletonEntityContext<T>` | `SingletonMonoEntity<T>` |
| `EntityContextEditor`    | `MonoEntityEditor`   |

Query tuples now yield `(IEntity Entity, ...)` instead of `(MonoEntity Entity, ...)`. If you have an explicit type annotation in a `foreach (var (MonoEntity entity, ...) in World.Query<T1, T2>())`, drop the type and let `var` infer — or change it to `IEntity`.
