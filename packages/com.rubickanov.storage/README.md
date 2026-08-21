# Storage

Key-value storage service with pluggable backends. Sync reads, async writes.

## Dependencies

> `UniTask` comes from a git URL, not from UPM — UPM will not pull it in for you. See [Third-party dependencies](https://github.com/rubickanov-org/unity-packages#third-party-dependencies).

- `UniTask` — return type for write operations
- `Microsoft.Extensions.Logging.Abstractions` — optional `ILogger<T>` passed into `FileStorageService` / `EncryptedStorageService` for write and decryption errors

## Architecture

```
IStorageService (sync Get, async Set)
├── PlayerPrefsStorageService   — Unity PlayerPrefs backend (Storage.Unity)
├── FileStorageService          — JSON file, in-memory reads, async disk writes (Storage.Runtime)
├── EncryptedStorageService     — AES-256-CBC decorator over any backend (Storage.Runtime)
├── PrefixedStorageService      — key-namespace decorator over any backend (Storage.Runtime)
└── NullStorageService          — no-op for server builds (Storage.Runtime)
```

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Storage.Runtime** | No | Interface, FileStorageService, EncryptedStorageService, PrefixedStorageService, NullStorageService |
| **Storage.Unity** | Yes | PlayerPrefsStorageService |

## Quick Start

Register a backend in your LifetimeScope. Two common starting points:

```csharp
// PlayerPrefs — simplest, good for settings
builder.Register<PlayerPrefsStorageService>(Lifetime.Singleton).As<IStorageService>();

// JSON file in persistentDataPath — good for save data
var path = Path.Combine(Application.persistentDataPath, "save.json");
builder.RegisterInstance<IStorageService>(new FileStorageService(path));
```

## Usage

### Reading Values

All reads are synchronous -- every backend keeps data in memory.

```csharp
float volume = storage.GetFloat("audio_master", 1f);
int highScore = storage.GetInt("high_score", 0);
string token = storage.GetString("auth_token", "");
bool exists = storage.HasKey("save_slot1");
```

### Writing Values

Writes return `UniTask`. Fire-and-forget for settings, await for important data.

```csharp
// Fire-and-forget (settings, preferences)
storage.SetFloat("audio_master", 0.8f).Forget();

// Awaited (save data, auth tokens)
await storage.SetString("save_slot1", json);

// Delete
await storage.DeleteKey("expired_token");
```

Fire-and-forget hands write errors to the logger (if one was passed into the backend constructor). If you need confirmation that the write actually landed on disk, `await` the call.

### Clearing Everything

```csharp
await storage.Clear();
```

Wipes every key the backend owns. For `PlayerPrefsStorageService` this calls `PlayerPrefs.DeleteAll()`, which clears **all** PlayerPrefs in the project -- not just keys this service wrote. Use with care; prefer a `FileStorageService` for isolated per-role storage if selective wipes matter.

### Backend Combinations

```csharp
// PlayerPrefs (simplest)
builder.Register<PlayerPrefsStorageService>(Lifetime.Singleton).As<IStorageService>();

// JSON file
var path = Path.Combine(Application.persistentDataPath, "settings.json");
builder.RegisterInstance<IStorageService>(new FileStorageService(path));

// Encrypted file (decorator)
var inner = new FileStorageService(path);
var encrypted = new EncryptedStorageService(inner, "my-passphrase");
builder.RegisterInstance<IStorageService>(encrypted);

// Server (no-op)
builder.Register<NullStorageService>(Lifetime.Singleton).As<IStorageService>();
```

## Multi-Storage Scoping

Roles like "settings", "save data", and "secure tokens" belong to your game code, not to this package. The package stays DI-agnostic and exposes a single `IStorageService` contract; you compose multiple instances however your game needs. Two patterns cover most cases and compose cleanly.

### Marker Interfaces

Declare one sub-interface per logical role in your game code and register each with its own backend. Consumers inject the marker interface they need -- typed and explicit, no string keys.

```csharp
// In your game code
public interface ISettingsStorage : IStorageService { }
public interface ISaveDataStorage : IStorageService { }
public interface ISecureStorage : IStorageService { }

// In your RootLifetimeScope
builder.Register<PlayerPrefsStorageService>(Lifetime.Singleton).As<ISettingsStorage>();

var savePath = Path.Combine(Application.persistentDataPath, "save.json");
builder.RegisterInstance<ISaveDataStorage>(new FileStorageService(savePath));

var tokenPath = Path.Combine(Application.persistentDataPath, "tokens.enc");
builder.RegisterInstance<ISecureStorage>(
    new EncryptedStorageService(new FileStorageService(tokenPath), passphrase));
```

```csharp
public class SaveSystem
{
    private readonly ISaveDataStorage _storage;
    public SaveSystem(ISaveDataStorage storage) => _storage = storage;
}
```

Swapping a backend -- local file today, cloud file tomorrow -- is one line in the registration.

### Prefixed Stores on a Single Backend

When multiple roles share one physical backend (one file, one PlayerPrefs namespace) and only the keys need to stay isolated, wrap with `PrefixedStorageService`. Keys are transparently namespaced; consumers see a normal `IStorageService`.

```csharp
var file = new FileStorageService(Path.Combine(Application.persistentDataPath, "game.json"));

builder.RegisterInstance<ISettingsStorage>(new PrefixedStorageService(file, "settings."));
builder.RegisterInstance<ISaveDataStorage>(new PrefixedStorageService(file, "save."));
```

`settings.volume` and `save.slot_1` live side by side in the same file without colliding. `Clear()` on a prefixed store throws `NotSupportedException` -- the interface doesn't expose key enumeration, so per-prefix wipes aren't possible without the inner backend.

Composition with `EncryptedStorageService` is the expected shape when secrets share a file with plaintext data:

```csharp
var file = new FileStorageService(path);
builder.RegisterInstance<ISecureStorage>(
    new PrefixedStorageService(
        new EncryptedStorageService(file, passphrase),
        "secure."));
```

## Design Decisions

- **Sync reads, async writes** — all backends load data into memory on construction. Reads never block. Only file writes go async (thread pool).
- **EncryptedStorageService as decorator** — wraps any `IStorageService`. AES-256-CBC with PBKDF2 key derivation. Stores encrypted values as Base64 strings via the inner backend's `SetString`/`GetString`.
- **No Unity dependency in Storage.Runtime** — `noEngineReferences: true`. PlayerPrefs support is isolated in Storage.Unity.
- **Thread-safety** — all methods must be called from a single thread (Unity main thread by convention). Concurrent mutations are not synchronized; `FileStorageService` only serializes *disk writes* internally, not in-memory mutations.
- **Concurrent file writes are chained** — `FileStorageService.SaveAsync` links every pending write into a single chain, so fire-and-forget calls cannot clobber each other on disk. Errors are logged via the optional `ILogger<FileStorageService>` passed into the constructor.
- **Corrupt JSON is quarantined** — if the file on disk fails to parse, `FileStorageService` renames it to `<path>.corrupt-<UTC-timestamp>.bak`, starts the session with an empty store, and logs a warning. The user loses that session's data but keeps a forensic artifact; silent overwrite is prevented.
- **PlayerPrefs writes are synchronous** — `PlayerPrefsStorageService` calls `PlayerPrefs.Save()` on every setter on the main thread; the returned `UniTask` is already completed. This trades async-uniformity for crash-safety (values survive a process kill).
- **Deterministic salt** — `EncryptedStorageService` derives its PBKDF2 salt from the passphrase itself; there is no per-installation randomness. Adequate for local client-side encryption of non-critical data (settings, save slots); not a replacement for server-side secret management.
- **Decryption failures return default** — a value that fails to decrypt or Base64-decode is treated the same as "no key"; the getter returns the default and logs a warning (if a logger was supplied). `HasKey` still reports `true`, so the two states stay distinguishable in code.
