# Aspect-Component System (ACS)

Entity composition framework for Unity. Aspects hold reactive data, components drive behavior, the entity ties them together. Unity-bound and pure-C# entities share one interface so logic runs the same in the Editor, a headless server, or an edit-mode test.

## Dependencies

- `R3` — reactive primitives (`ReactiveProperty<T>`, `Subject<T>`, `DisposableBag`)
- `ObservableCollections` + `ObservableCollections.R3` — reactive collections for aspect fields (`ObservableList<T>`, `ObservableDictionary<TKey,TValue>`, `ObservableHashSet<T>`, `ObservableRingBuffer<T>`)

## Architecture

```
IEntity                           IEntityAspect            IEntityLogic / ITickable
    ├── Entity      (pure C#)     (marker, data only)      (pure C#, optional)
    ├── MonoEntity  (MonoBehaviour)
    └── World       (pure C#)            ▲
            ▲                            │   Require<T>() / [Aspect] injection
            └── MonoWorld  (singleton MonoEntity)
                            │
                            │  owns
                            ▼
                      EntityRegistry        — Type → entities (Query<T>)
                                            — EntityId → entity (TryFindById)
```

Three tiers of entity behavior:

1. **Reactive-only** (~80%) — `IEntityLogic` attached via `entity.AttachLogic(...)`. One plain C# class, auto-disposed when the entity is destroyed.
2. **Tickable** (~15%) — `ITickable` driven by `EntityTickRunner` (Unity) or a headless loop (console host, fixed-step server). Same class, different frame source.
3. **Unity-bound** (~5%) — `EntityComponent : MonoBehaviour` for behaviour that genuinely needs `Transform`, `Rigidbody`, `Animator`, `Canvas`, etc.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **ACS.Runtime** | Yes | Core framework and Unity adapters: `Entity`, `MonoEntity`, `World`, `MonoWorld`, `EntityQuery`, injectors, tick runner |
| **ACS.Editor** | Editor | `MonoEntity` inspector with aspect usage analysis |

## Core Concepts

**Aspect** — Pure data container that implements **IEntityAspect**. Only holds reactive fields and event signals. Zero logic, zero methods.

**Component** — Single unit of behavior that reads and writes aspects. Extends **EntityComponent** (MonoBehaviour). One component, one job.

**IEntity** — The aspect-container contract: `Id`, `Require<T>`, `TryGet<T>`, `Has<T>`, `GetAllAspects`, `AspectTypes`, `Destroyed`. Implemented by the Unity-bound `MonoEntity` and the pure POCO `Entity` (and by `World` itself). Depend on `IEntity` — never on `MonoEntity` — unless you actually need Unity.

**MonoEntity** — MonoBehaviour on the entity root. Components obtain aspects via `[Aspect]` field injection or `Context.Require<T>()`. Allocates its `Id` and registers with `World.Current` in `Awake`; fires `Destroyed` in `OnDestroy`.

**Entity** — Pure C# `IEntity` for pocket entities (item in an inventory, buff without a visual), headless simulations, and edit-mode tests. Pass a `World` into the constructor to auto-register; otherwise stays standalone. Lifetime ends with `Dispose()`.

**World** — Pure-C# `IEntity` that owns world-scoped aspects and the entity registry. Exposes a static `Current` slot (one active world at a time) that backs `World.Require<T>()`, `World.Query<T>()`, and `World.TryFindById(...)` — all throw `InvalidOperationException` if no world is active, so call sites stay honest. Has no Unity dependencies — construct one directly for headless simulations.

**MonoWorld** — `SingletonMonoEntity<MonoWorld>` that owns a `World` instance, assigns it as `World.Current` in `Awake`, and clears it in `OnDestroy`. Drop one on a scene GameObject and every `MonoEntity` auto-registers with `MonoWorld.Instance.World`. All `IEntity` calls on the MonoWorld delegate into the embedded `World` — there is no duplicate aspect store.

**EntityId** — Session-local stable identifier allocated at entity construction. Never reused within a session; `EntityId.None` represents "no entity". Serves as the key for `World.TryFindById(...)` and as the routing handle used by extension packages (`acs.netcode`, future `acs.persistence`).

**EntityRef** — Unmanaged `struct` wrapper around `EntityId` for aspect fields that reference other entities (AI target, parent, projectile owner). `TryResolve(world, out var entity)` hits the by-id registry each call — no cached reference, so a destroyed target is always observable as "dangling", never returned as a stale pointer. Replication-safe (`acs.netcode` passes it through unchanged).

## Quick Start

1. Drop a **MonoWorld** on a GameObject in the scene. It wires up `World.Current` so queries and world-scoped aspects work.
2. Add a **MonoEntity** to the root GameObject of each entity.
3. Define an aspect:

```csharp
public class HealthAspect : IEntityAspect
{
    public readonly ReactiveProperty<int> CurrentHealth = new(100);
    public readonly ReactiveProperty<bool> IsAlive = new(true);
    public readonly Subject<DamageInfo> Hit = new();
}
```

4. Write a component that reacts to it:

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

The `[Aspect]` attribute resolves the field from the entity context in `Awake`. `OnSubscribe` wires subscriptions that are automatically disposed on `OnDisable`.

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

Mark fields with `[Aspect]` — injection happens in `EntityComponent.Awake` via reflection (cached per component type, compiled `Expression` delegates for the `Require<T>` call). Multiple components requesting the same type share the same instance:

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

For custom init at Awake time, override `OnAwake` — never `Awake` itself. `Awake` is non-virtual on `EntityComponent` specifically to block the "forgot `base.Awake()` → silent NRE" failure mode; the compiler rejects `override void Awake` on subclasses, and `OnAwake` is invoked after `[Aspect]` injection completes:

```csharp
protected override void OnAwake()
{
    // [Aspect] fields are already populated here.
}
```

For `SingletonMonoEntity<T>` subclasses (including `MonoWorld`), `Awake` stays virtual and you do still override it — `base.Awake()` assigns the static `Instance`. Skip the base call and `MonoWorld.Instance` / `YourSingleton.Instance` stay `null`:

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

With a `MonoWorld` in the scene, world-scoped aspects work exactly like entity aspects:

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

Components inject world aspects via the same static accessor inside `OnSubscribe`:

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

`World.Query<T>()` returns every aspect of that type currently in the world. Multi-argument overloads (up to 8) yield tuples of `(entity, aspect1, aspect2, ...)`:

```csharp
// All living health aspects — plain foreach keeps the query's struct enumerator zero-alloc.
foreach (var health in World.Query<HealthAspect>())
{
    if (health.CurrentHealth.Value <= 0) continue;
    // ...
}

// Entities carrying both health and position
foreach (var (entity, health, pos) in World.Query<HealthAspect, PositionAspect>())
    Debug.Log($"{entity} @ {pos.Value.Value}, hp={health.CurrentHealth.Value}");
```

Registration is automatic — every `Require<T>` call on any `MonoEntity` registers with `World.Current`. Destruction unregisters.

### Entity References — EntityId and EntityRef

Every `IEntity` gets a session-local `EntityId` at construction. An `EntityRef` is the value-type wrapper you store on aspect data when a field *means* "a reference to another entity" (AI target, projectile owner, parent socket):

```csharp
public class AiTargetAspect : IEntityAspect
{
    public readonly ReactiveProperty<EntityRef> Target = new(EntityRef.None);
}

// Acquire a target
aspect.Target.Value = EntityRef.From(playerEntity);

// Consume it — resolve each time rather than caching, so destroyed targets are
// observable as "dangling" and can't become a stale pointer in AI logic.
if (aspect.Target.Value.TryResolve(World.Current!, out var target))
    MoveToward(target);
```

Use raw `EntityId` only for infrastructure (registry keys, save payloads, network messages). `EntityRef` is the domain-facing type and the one replication ships natively.

For direct by-id lookup (save/load bridges, debug tools, ad-hoc probes):

```csharp
if (World.Current!.TryFindById(savedId, out var entity))
    RestoreInto(entity);
```

`TryFindById` returns `false` for `EntityId.None`, for ids belonging to a different world, and for destroyed entities.

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

**IEntityLogic** is the pure-C# equivalent of `EntityComponent`: wire subscriptions in the constructor, release them in `Dispose`. `AttachLogic(entity, logic)` hooks `Destroyed` for you, so the logic disposes automatically when the owning entity dies. `Dispose` must be idempotent — if the caller disposes manually and the entity is later destroyed, the hook runs again:

```csharp
public sealed class DeathWatchLogic : IEntityLogic
{
    private readonly IDisposable _sub;
    private bool _disposed;

    public DeathWatchLogic(HealthAspect health)
        => _sub = health.CurrentHealth.Subscribe(v => { if (v <= 0) OnDied(); });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sub.Dispose();
    }

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

For scene-wide queries without a `MonoWorld` in the scene, construct a pure `World` directly and pass it into each `Entity` — they auto-register on construction (by id) and on each first `Require<T>()` (per-aspect), and auto-unregister on `Dispose`:

```csharp
var world = new World();

var hero = new Entity(world);
hero.Require<HealthAspect>();
hero.Require<PositionAspect>();

foreach (var (owner, health, pos) in world.QueryLocal<HealthAspect, PositionAspect>())
    // ...

hero.Dispose(); // auto-unregisters from per-aspect buckets and the by-id index.
```

`QueryLocal<...>` is the instance-scoped counterpart of the static `World.Query<...>` — use it when the world you care about is not `World.Current` (a pocket world running alongside the main one, a server-side mini-sim, a test). The static form dispatches to `QueryLocal` on `Current`.

If you want finer control (pocket entities that join a registry only conditionally, headless sims that drive registration externally), use the parameterless `new Entity()` ctor and call `world.Register(entity)` / `world.Register(entity, typeof(T))` / `world.Unregister(...)` yourself.

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

`MonoEntity.OnAspectCreated` fires once for every new aspect, including those created lazily via `Require` after `Start` (e.g. from `OnEnable`, `Update`, or delayed logic). World-scoped aspects created on `MonoWorld.Instance` also flow through this event. Use it to react to aspects that may appear at any time during an entity's life:

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

MonoWorld (SingletonMonoEntity<MonoWorld>)
├── TimeAspect             — global time of day
└── WeatherAspect          — current weather
```

## Design Decisions

- **MonoEntity lazy-creates aspects** — `Require<T>()` returns an existing instance or creates a new one. No manual registration, no initialization order coupling.
- **`[Aspect]` injection over `Require<T>` in Awake** — eliminates boilerplate, keeps the Awake override optional, and makes aspect dependencies a declarative part of the component's shape.
- **`OnSubscribe` hook instead of manual `OnEnable`/`OnDisable`** — guarantees subscriptions are always paired with disposal. Component authors cannot forget to dispose.
- **MonoWorld is a MonoEntity** — replication (`[Replicated]`), persistence, and inspector tooling all work on world aspects for free. No separate "world component" abstraction.
- **Pure-C# World, Unity adapter on top** — identity/composition (aspects, registry, queries, by-id lookup) is plain C# via `World`. `MonoWorld` is a thin delegating adapter that assigns `World.Current` and forwards events. Headless simulations and fast edit-mode tests never touch `MonoBehaviour`.
- **Queries ship in core, spatial queries do not** — `World.Query<T>()` is a type-bucket lookup with no physics dependency. Spatial queries (radius, nearest, grid) live in a separate package to keep core dependency-free.
- **`EntityRef` resolves on every access** — no cached `IEntity` inside the struct. A managed field would break replication, and a cache would go stale after the target is destroyed. The by-id registry lookup is O(1), so the always-resolve policy costs nothing and keeps dangling references observable.
- **Static `EntityInjector` delegate instead of DI dependency** — keeps the package zero-DI. Any framework (VContainer, Zenject) plugs in with one line.
- **Netcode lives in `acs.netcode`** — the core package has no NGO dependency. Replication, RPCs, and ownership wire themselves via the static extension hooks.
