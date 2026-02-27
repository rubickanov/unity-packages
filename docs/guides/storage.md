# com.rubickanov.storage

Key-value storage service with pluggable backends. Sync reads, async writes.

## Architecture

```
IStorageService (sync Get, async Set)
├── PlayerPrefsStorageService   — Unity PlayerPrefs (Storage.Unity)
├── FileStorageService          — JSON file, sync read from memory, async write (Storage.Runtime)
├── EncryptedStorageService     — AES-256-CBC decorator over any backend (Storage.Runtime)
└── NullStorageService          — no-op for server builds (Storage.Runtime)
```

## Assemblies

- **Storage.Runtime** (`noEngineReferences: true`) — interface, NullStorageService, FileStorageService, EncryptedStorageService. Pure C#, depends only on UniTask.
- **Storage.Unity** — PlayerPrefsStorageService. Depends on UnityEngine.

## Usage

```csharp
// Read (sync)
float volume = storage.GetFloat("audio_master", 1f);

// Write (fire-and-forget for settings)
storage.SetFloat("audio_master", 0.8f).Forget();

// Write (awaited for important data)
await storage.SetString("save_slot1", json);
```

## Backend combinations

```csharp
// PlayerPrefs (default):
builder.Register<PlayerPrefsStorageService>(Lifetime.Singleton).As<IStorageService>();

// Encrypted file:
var path = Path.Combine(persistentDataPath, "save.dat");
IStorageService storage = new EncryptedStorageService(new FileStorageService(path), passphrase);
builder.RegisterInstance(storage).As<IStorageService>();

// Server (no-op):
builder.Register<NullStorageService>(Lifetime.Singleton).As<IStorageService>();
```
