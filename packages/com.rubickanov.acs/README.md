# Aspect-Component System (ACS)

Entity composition framework for Unity. Aspects hold reactive data, components drive behavior, EntityContext ties them together.

## Dependencies

None.

## Architecture

```
IEntityAspect (marker interface)
    ^
    |  Require<T>()
EntityContext (aspect registry per entity)
    ^
    |  Context property
EntityComponent (MonoBehaviour base class)
```

**EntityContext** lives on the root GameObject and lazily creates aspects on first `Require<T>()` call. Components on the same entity (or children) share aspects through the context — no direct references between components.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **ACS.Runtime** | Yes | Core framework: EntityContext, EntityComponent, EntityInjector |
| **ACS.Editor** | Editor | EntityContext inspector with aspect usage analysis |

## Core Concepts

**Aspect** — Pure data container that implements **IEntityAspect**. Only holds reactive fields and event signals. Zero logic, zero methods.

**Component** — Single unit of behavior that reads and writes aspects. Extends **EntityComponent** (MonoBehaviour). One component, one job.

**EntityContext** — MonoBehaviour on the entity root that serves as the shared aspect registry. Components obtain aspects via `Context.Require<T>()`.

## Quick Start

1. Add **EntityContext** to the root GameObject of your entity.
2. Define an aspect (pure data):

```csharp
public class HealthAspect : IEntityAspect
{
    public readonly ReactiveProperty<int> CurrentHealth = new(100);
    public readonly ReactiveProperty<bool> IsAlive = new(true);
    public readonly Subject<DamageInfo> Hit = new();
}
```

3. Write a component that uses it:

```csharp
public class DamageFlashObserver : EntityComponent
{
    private HealthAspect _health = default!;
    private DisposableBag _disposables;

    protected override void Awake()
    {
        base.Awake();
        _health = Context.Require<HealthAspect>();
    }

    private void OnEnable()
    {
        _health.Hit.Subscribe(OnHit).AddTo(ref _disposables);
    }

    private void OnDisable()
    {
        _disposables.Dispose();
    }

    private void OnHit(DamageInfo info) { /* flash logic */ }
}
```

## Usage

### Defining Aspects

Aspects are pure data. Only `ReactiveProperty<T>` fields and `Subject<T>` events:

```csharp
public class MovementAspect : IEntityAspect
{
    public readonly ReactiveProperty<Vector3> Velocity = new(Vector3.zero);
    public readonly ReactiveProperty<float> MoveSpeed = new(5f);
    public readonly ReactiveProperty<bool> IsGrounded = new(true);
}
```

### Obtaining Aspects

Components get aspects from the context in `Awake()`. Multiple components requesting the same aspect type get the same instance:

```csharp
protected override void Awake()
{
    base.Awake();
    _movement = Context.Require<MovementAspect>();
}
```

### Querying Aspects

**EntityContext** provides `TryGet<T>()` for optional aspects and `Has<T>()` for existence checks:

```csharp
if (Context.TryGet<ShieldAspect>(out var shield))
    shield.CurrentShield.Value -= overflow;

if (Context.Has<FlyingAspect>())
    ApplyGravity = false;
```

### Component Lifecycle

Subscribe in `OnEnable()`, dispose in `OnDisable()`:

```csharp
private void OnEnable()
{
    _health.CurrentHealth.Subscribe(OnHealthChanged).AddTo(ref _disposables);
}

private void OnDisable()
{
    _disposables.Dispose();
}
```

### Dependency Injection

ACS does not depend on any DI framework. **EntityInjector** is a static delegate that components call in `Awake()`. Wire it from your container:

```csharp
EntityInjector.Inject = go =>
{
    var scope = LifetimeScope.Find<LifetimeScope>(go.scene);
    scope?.Container.InjectGameObject(go);
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
```

## Design Decisions

- **EntityContext lazy-creates aspects** — `Require<T>()` returns an existing instance or creates a new one. No manual registration needed. Component initialization order does not matter.
- **Static EntityInjector delegate instead of DI dependency** — keeps the package zero-dependency. Any DI framework (VContainer, Zenject) can plug in with one line.
- **No EntityNetworkComponent in this package** — netcode support lives in a separate ACS.Netcode extension to avoid a hard dependency on Netcode for GameObjects.
