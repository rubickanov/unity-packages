# ACS - Netcode Extension

Netcode for GameObjects extension for ACS. Provides `EntityNetworkComponent` base class for network-aware components.

## Requirements

- ACS (`com.rubickanov.acs`)
- Netcode for GameObjects (`com.unity.netcode.gameobjects`)

## Usage

Inherit from `EntityNetworkComponent` instead of `EntityComponent` for components that need network authority:

```csharp
public class MyNetworkComponent : EntityNetworkComponent
{
    protected override void OnNetworkSpawn()
    {
        // Subscribe to aspects here
    }

    protected override void OnNetworkDespawn()
    {
        // Unsubscribe here
    }
}
```
