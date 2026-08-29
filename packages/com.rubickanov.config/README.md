# Config

Type-safe config loading with caching, validation, and catalog refresh for remote content updates. Backed by Unity Addressables by default; swap the loader to test without the Addressables runtime.

## Dependencies

> `UniTask` comes from a git URL, not from UPM — UPM will not pull it in for you. See [Third-party dependencies](https://github.com/rubickanov/unity-packages#third-party-dependencies).

- `UniTask` — async load operations
- `Unity.Addressables` / `Unity.ResourceManager` — default asset loader backend
- `UniTask.Addressables` — `WithCancellation` on Addressables handles
- `Microsoft.Extensions.Logging.Abstractions` — logging abstraction (plug in any backend: ZLogger, Serilog, etc.)

## Architecture

```
IConfigService ──► [RegisterConfig("address")]
       │                    │
       ▼                    ▼
 ConfigService ──► IAssetLoader ──► AddressablesAssetLoader (default)
 (cache + pending)                  fake loader (tests)
       │
       ▼
 ConfigBase (ScriptableObject)
       ├── ConfigDatabase<T>
       └── (game configs)
```

**ConfigService** loads configs by type, resolving each type's Addressable address from its `[RegisterConfig]` attribute via a pluggable `IAssetLoader`. Loaded configs are cached by type, so `Get<T>()` returns the same instance without re-loading. Concurrent `LoadAsync<T>()` calls for the same type are coalesced — the asset is fetched once and shared. `ReleaseAll()` frees every tracked loader handle for clean scene transitions.

## Core Concepts

**ConfigBase** — Base class for all configs. A `ScriptableObject` with a virtual `Validate()` hook called after load.

**RegisterConfig** — Class attribute that binds a config type to its Addressable address. There is no central path registry; each type declares its own address.

**IAssetLoader** — Abstraction over asset loading. `LoadAsync` returns the asset plus an opaque release token that the service hands back to `Release` later.

## Quick Start

1. Declare a config and its address:

```csharp
[RegisterConfig("Configs/GameSettings")]
[CreateAssetMenu(menuName = "Configs/Game Settings")]
public class GameSettings : ConfigBase
{
    [SerializeField] private float _difficulty = 1f;
    public float Difficulty => _difficulty;
}
```

2. Register the service. `ConfigService` takes an `ILoggerFactory` and an `IAssetLoader`:

```csharp
builder.Register<ILoggerFactory>(_ => NullLoggerFactory.Instance, Lifetime.Singleton);
builder.Register<IAssetLoader, AddressablesAssetLoader>(Lifetime.Singleton);
builder.Register<IConfigService, ConfigService>(Lifetime.Singleton);
```

3. Load, then read:

```csharp
var settings = await configService.LoadAsync<GameSettings>();

// Synchronous access after load (throws if not loaded yet):
var same = configService.Get<GameSettings>();

// Non-throwing lookup:
if (configService.TryGet<GameSettings>(out var maybe)) { /* ... */ }
```

## Usage

### Loading Configs

`LoadAsync<T>()` resolves the address from `[RegisterConfig]`, loads via the `IAssetLoader`, validates, and caches. A second call for the same type returns the cached instance.

```csharp
var settings = await configService.LoadAsync<GameSettings>();
var again = configService.Get<GameSettings>();   // same instance, no re-load
```

### Cancellation

Each caller passes its own token. Cancelling one awaiter does not fault others waiting on the same coalesced load — the shared load always runs to completion and caches.

```csharp
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var settings = await configService.LoadAsync<GameSettings>(cts.Token);
```

### Validation

Override `Validate()` to reject bad data. When it returns `false`, `LoadAsync<T>()` releases the handle and throws `InvalidOperationException` — the invalid config never enters the cache.

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

### Databases of Items

`ConfigDatabase<T>` holds a collection of `IIdentifiable` config items and adds O(1) `Get(id)` lookup. Each item is its own `ConfigBase` asset; the database references them in a serialized list assigned in the inspector.

```csharp
[RegisterConfig("Configs/Items")]
[CreateAssetMenu(menuName = "Configs/Item Database")]
public class ItemDatabase : ConfigDatabase<ItemData> { }

[CreateAssetMenu(menuName = "Configs/Item")]
public class ItemData : ConfigBase, IIdentifiable
{
    [SerializeField] private string _id;
    [SerializeField] private int _price;

    public string Id => _id;
    public int Price => _price;
}
```

```csharp
var db = await configService.LoadAsync<ItemDatabase>();

ItemData sword = db.Get("sword");          // null if not found
IReadOnlyList<ItemData> all = db.All;      // insertion order preserved
```

`ConfigDatabase<T>.Validate()` flags empty and duplicate `Id` values, logging the offending indices/ids and returning `false` — so an invalid database throws at `LoadAsync` time instead of being cached. The `Get` lookup dictionary is built lazily on first call and also throws on duplicate ids.

### Remote Content Updates

There is no in-place hot reload. To pick up new content, release, refresh the catalog, then reload between scenes:

```csharp
configService.ReleaseAll();
await configService.RefreshCatalogIfNeededAsync();
var fresh = await configService.LoadAsync<GameSettings>();
```

### Cleanup

`ReleaseAll()` frees all cached handles but keeps the service usable. `Dispose()` releases everything and makes every subsequent call throw `ObjectDisposedException`.

```csharp
configService.ReleaseAll();   // between scenes
configService.Dispose();      // shutting down
```

### Testing

`ConfigService` depends only on `IAssetLoader`, not on Addressables. Supply a fake loader (e.g. one backed by a `Dictionary<string, ScriptableObject>`) to exercise loading, caching, and validation in EditMode tests without the Addressables runtime.

## Design Decisions

- **Attribute-based addresses** — each config type declares its own Addressable address via `[RegisterConfig]`; no centralized path registry.
- **Pluggable loader** — `IAssetLoader` decouples the service from Addressables, keeping EditMode tests fast and the production path simple.
- **Fail-fast validation** — invalid configs never reach the consumer; the service releases the handle and throws.
- **Coalesced concurrent loads** — duplicate `LoadAsync<T>()` calls for the same type share one underlying load, on an internal token so per-caller cancellation stays independent.
- **No hot reload** — updates happen between scenes via `ReleaseAll()` + `RefreshCatalogIfNeededAsync()` + `LoadAsync()`. Simpler than reactive in-place reload.
- **ConfigDatabase O(1) lookup** — lazy `Dictionary` built on first `Get()`; rebuilt in the editor after inspector changes via `OnValidate`.
