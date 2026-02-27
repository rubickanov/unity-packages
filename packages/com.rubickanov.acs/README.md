# Aspect-Component System (ACS)

Lightweight entity composition framework for Unity. Zero external dependencies.

## Core Concepts

**Aspect** — Pure data container. Only holds reactive fields and event signals. No logic, no methods.

**Component** — Single unit of behavior that reads and writes aspects. One component, one job.

**EntityContext** — MonoBehaviour that serves as the aspect registry for an entity. Components obtain aspects via `Context.Require<T>()`.

## Package Structure

| Assembly | Description | Dependencies |
|---|---|---|
| `ACS.Runtime` | Core framework (EntityContext, EntityComponent, aspects, injector) | Unity only |
| `ACS.Runtime.Network` | NetworkBehaviour base class for networked components | ACS.Runtime, Netcode for GameObjects |
| `ACS.Editor` | Inspector tooling (aspect viewer, usage analyzer) | ACS.Runtime |

## Quick Start

### Define an Aspect

```csharp
public class HealthAspect : IEntityAspect
{
    public readonly ReactiveProperty<int> CurrentHealth = new(100);
    public readonly ReactiveProperty<bool> IsAlive = new(true);
    public readonly Subject<DamageInfo> Hit = new();
}
```

### Write a Component

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

    private void OnHit(DamageInfo info)
    {
        // flash logic
    }
}
```

### Assemble an Entity

Add `EntityContext` to the root GameObject. Add aspect-driven components as needed. Components discover each other through shared aspects — no direct references.

## Dependency Injection

ACS does not depend on any DI framework. Integration is done via `EntityInjector.Inject` — a static delegate that components call in `Awake()`. Set it up from your DI container:

```csharp
EntityInjector.Inject = go =>
{
    var scope = LifetimeScope.Find<LifetimeScope>(go.scene);
    scope?.Container.InjectGameObject(go);
};
```

## Rules

- Aspects are pure data. Zero logic.
- Components communicate only through aspects, never through direct references.
- One component, one job. Prefer three small components over one large one.
- Subscribe in `OnEnable`, dispose in `OnDisable` (for `EntityComponent`).
- Subscribe in `OnNetworkSpawn`, dispose in `OnNetworkDespawn` (for `EntityNetworkComponent`).
- Services do not know about aspects. Components bridge services and aspects.

## Requirements

- Unity 2022.3+
- Netcode for GameObjects (only if using `ACS.Runtime.Network`)
