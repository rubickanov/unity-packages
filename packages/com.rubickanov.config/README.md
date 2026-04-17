# Config

Type-safe config loading with caching, validation, and catalog refresh for remote content updates. Backed by Unity Addressables by default; swap the loader to test without the Addressables runtime.

## Dependencies

- `UniTask` — async operation execution
- `Unity.Addressables` — default asset loader backend
- `Microsoft.Extensions.Logging.Abstractions` — logging abstraction (plug in any backend: ZLogger, Serilog, etc.)

## Architecture

```
IConfigService ──► [RegisterConfig("address")]
       │                    │
       ▼                    ▼
 ConfigService ──► IAssetLoader ──► AddressablesAssetLoader (default)
 (cache + pending)                  FakeAssetLoader (tests)
       │
       ▼
 ConfigBase (ScriptableObject)
       ├── ConfigDatabase<T>
       └── (game configs)
```

**ConfigService** loads configs by type, resolving addresses from `[RegisterConfig]` attributes via a pluggable `IAssetLoader`. Concurrent `LoadAsync<T>()` calls for the same type are coalesced — the asset is fetched once. `ReleaseAll()` frees all tracked handles for clean scene transitions.

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
builder.Register<ILoggerFactory>(_ => NullLoggerFactory.Instance, Lifetime.Singleton);
builder.Register<IAssetLoader, AddressablesAssetLoader>(Lifetime.Singleton);
builder.Register<IConfigService, ConfigService>(Lifetime.Singleton);
```

3. Load and use:

```csharp
var config = await configService.LoadAsync<GameSettings>();

// Synchronous access after load (throws if not loaded):
var same = configService.Get<GameSettings>();

// Non-throwing lookup:
if (configService.TryGet<GameSettings>(out var maybe)) { /* ... */ }
```

## Usage

### Loading Configs

```csharp
var settings = await configService.LoadAsync<GameSettings>();
var again = configService.Get<GameSettings>();
```

### Cancellation

```csharp
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var settings = await configService.LoadAsync<GameSettings>(cts.Token);
```

### Remote Content Updates

```csharp
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
public class ItemData : ConfigBase, IIdentifiable
{
    [SerializeField] private string _id;
    [SerializeField] private int _price;

    public string Id => _id;
    public int Price => _price;
}

var db = configService.Get<ItemDatabase>();
var sword = db.Get("sword");       // O(1) lookup
var allItems = db.All;             // IReadOnlyList<ItemData>
```

`ConfigDatabase<T>.Validate()` flags duplicate or empty `Id` values. With the default fail-fast policy, invalid databases throw at `LoadAsync` time instead of being silently cached.

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

`LoadAsync<T>()` throws `InvalidOperationException` and releases the handle when `Validate()` returns `false` — the invalid config never enters the cache.

### Cleanup

```csharp
configService.ReleaseAll();
configService.Dispose();
```

After `Dispose()` every method on the service throws `ObjectDisposedException`.

### Testing

Provide a custom `IAssetLoader` (e.g. a fake backed by a `Dictionary<string, ScriptableObject>`) to test code that depends on `IConfigService` without bringing up the Addressables runtime.

## Design Decisions

- **Attribute-based addresses** — each config type declares its own Addressable address via `[RegisterConfig]`. No centralized path registry.
- **Pluggable loader** — `IAssetLoader` decouples the service from Addressables, keeping EditMode tests fast and the production path simple.
- **Fail-fast validation** — invalid configs never reach the consumer; the service throws and releases the handle.
- **Coalesced concurrent loads** — duplicate `LoadAsync<T>()` calls for the same type share a single underlying load.
- **No hot reload** — config updates happen between scenes via `ReleaseAll()` + `RefreshCatalogIfNeededAsync()` + `LoadAsync()`. Simpler than reactive in-place reload.
- **ConfigDatabase O(1) lookup** — lazy `Dictionary` built on first `Get()` call; rebuilt in the editor after inspector changes.
