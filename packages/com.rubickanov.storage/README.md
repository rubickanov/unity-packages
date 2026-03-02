# Storage

Key-value storage service with pluggable backends. Sync reads, async writes.

## Dependencies

- `UniTask` — async write operations

## Architecture

```
IStorageService (sync Get, async Set)
├── PlayerPrefsStorageService   — Unity PlayerPrefs backend (Storage.Unity)
├── FileStorageService          — JSON file, in-memory reads, async disk writes (Storage.Runtime)
├── EncryptedStorageService     — AES-256-CBC decorator over any backend (Storage.Runtime)
└── NullStorageService          — no-op for server builds (Storage.Runtime)
```

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Storage.Runtime** | No | Interface, FileStorageService, EncryptedStorageService, NullStorageService |
| **Storage.Unity** | Yes | PlayerPrefsStorageService |

## Quick Start

Register a backend in your LifetimeScope:

```csharp
builder.Register<PlayerPrefsStorageService>(Lifetime.Singleton).As<IStorageService>();
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

## Design Decisions

- **Sync reads, async writes** — all backends load data into memory on construction. Reads never block. Only file writes go async (thread pool).
- **EncryptedStorageService as decorator** — wraps any `IStorageService`. AES-256-CBC with PBKDF2 key derivation. Stores encrypted values as Base64 strings via the inner backend's `SetString`/`GetString`.
- **No Unity dependency in Storage.Runtime** — `noEngineReferences: true`. PlayerPrefs support is isolated in Storage.Unity.
