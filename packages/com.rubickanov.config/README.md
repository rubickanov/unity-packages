# Config

Type-safe config loading via Addressables with caching, validation, and catalog refresh for remote content updates.

## Dependencies

- `UniTask` — async operation execution
- `Unity.Addressables` — asset loading
- `ZLogger` — structured logging

## Architecture

```
IConfigService ──► [RegisterConfig("address")]
       │                    │
       ▼                    ▼
 ConfigService         ConfigBase (ScriptableObject)
 (cache + handles)          │
                            ├── ConfigDatabase<T>
                            └── (game configs)
```

**ConfigService** loads configs by type, resolving addresses from `[RegisterConfig]` attributes. Loaded assets are cached with Addressable handle tracking. `ReleaseAll()` frees all handles for clean scene transitions.

## Quick Start

1. Create a config:

```csharp
[RegisterConfig("Configs/GameSettings")]
[CreateAssetMenu(fileName = "GameSettings", menuName = "Configs/GameSettings")]
public class GameSettings : ConfigBase
{
    [SerializeField] private float _difficulty = 1f;
    public float Difficulty => _difficulty;
}
```

2. Register in your LifetimeScope:

```csharp
builder.Register<IConfigService, ConfigService>(Lifetime.Singleton);
```

3. Load and use:

```csharp
var config = await configService.LoadAsync<GameSettings>();

// Later, synchronous access (throws if not loaded):
var config = configService.Get<GameSettings>();
```

## Usage

### Loading Configs

```csharp
// Async load (caches automatically):
var settings = await configService.LoadAsync<GameSettings>();

// Synchronous access to already-loaded config:
var settings = configService.Get<GameSettings>();
```

### Remote Content Updates

```csharp
// Between scenes:
configService.ReleaseAll();
await configService.RefreshCatalogIfNeededAsync();
var freshConfig = await configService.LoadAsync<GameSettings>();
```

### ConfigDatabase for Collections

```csharp
[RegisterConfig("Configs/Items")]
public class ItemDatabase : ConfigDatabase<ItemData>
{
}

[Serializable]
public class ItemData : IIdentifiable
{
    [SerializeField] private string _id;
    [SerializeField] private int _price;

    public string Id => _id;
    public int Price => _price;
}

// Usage:
var db = configService.Get<ItemDatabase>();
var sword = db.Get("sword");       // O(1) lookup
var allItems = db.All;             // IReadOnlyList<ItemData>
```

### Validation

```csharp
[RegisterConfig("Configs/Balance")]
public class BalanceConfig : ConfigBase
{
    [SerializeField] private int _maxHp = 100;

    public override bool Validate()
    {
        if (_maxHp <= 0)
        {
            Debug.LogError("BalanceConfig: MaxHp must be positive");
            return false;
        }
        return true;
    }
}
```

### Cleanup

```csharp
// Release all cached configs and Addressable handles:
configService.ReleaseAll();

// Or via IDisposable:
configService.Dispose();
```

## Design Decisions

- **Attribute-based addresses** — each config type declares its own Addressable address via `[RegisterConfig]`. No centralized path registry.
- **Caching with handle tracking** — loaded configs are cached by type. Addressable handles are tracked for proper release.
- **No hot reload** — config updates happen between scenes via `ReleaseAll()` + `RefreshCatalog` + `LoadAsync`. Simpler than reactive in-place reload.
- **ConfigDatabase O(1) lookup** — lazy `Dictionary` built on first `Get()` call.
