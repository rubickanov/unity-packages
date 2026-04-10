# ACS Netcode

Netcode for GameObjects extension for [ACS](../com.rubickanov.acs/). Provides **EntityNetworkComponent** base class for network-aware entity components.

## Dependencies

- `com.rubickanov.acs` — base ACS framework
- `com.unity.netcode.gameobjects` — Netcode for GameObjects

## Quick Start

Inherit from **EntityNetworkComponent** instead of **EntityComponent** for components that need **NetworkBehaviour** capabilities (RPCs, NetworkVariables, ownership checks):

```csharp
public class NetworkHealthSync : EntityNetworkComponent
{
    private HealthAspect _health = default!;

    protected virtual void Awake()
    {
        base.Awake();
        _health = Context.Require<HealthAspect>();
    }

    public override void OnNetworkSpawn()
    {
        // Subscribe to aspects here
        _health.Current.Changed += OnHealthChanged;
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe here
        _health.Current.Changed -= OnHealthChanged;
    }

    private void OnHealthChanged(float value) { /* sync logic */ }
}
```

## Usage

**EntityNetworkComponent** extends `NetworkBehaviour` and implements `IEntityComponent`. It provides access to `Context` (lazily resolved via `GetComponentInParent<EntityContext>()`) and calls `EntityInjector.Inject` in `Awake()` for DI support.

Subscribe in `OnNetworkSpawn()`, unsubscribe in `OnNetworkDespawn()` -- same lifecycle pattern as standard Netcode components but with ACS aspect access.

## IL2CPP Support

Built-in unmanaged types (`int`, `float`, `bool`, `double`, `Vector2`, `Vector3`, `Vector4`, `Quaternion`, `Color`) work on IL2CPP automatically via AOT hints.

If you use **custom unmanaged structs** in `[ReplicatedState]` / `[ReplicatedEvent]`, add a `link.xml` to your project's `Assets/` to prevent stripping:

```xml
<linker>
  <assembly fullname="ACS.Runtime.Netcode" preserve="all"/>
</linker>
```
