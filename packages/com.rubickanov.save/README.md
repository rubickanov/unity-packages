# Save

One store for everything a game keeps of the player's, named by game-declared area and name instead of a path: async like console saves, every write whole or not at all, swapped as one piece when the game moves to another platform.

## Dependencies

> `UniTask` comes from a git URL, not from UPM — UPM will not pull it in for you. See [Third-party dependencies](https://github.com/rubickanov/unity-packages#third-party-dependencies).

- `UniTask` — every store call is a `UniTask`
- `com.unity.modules.unitywebrequesttexture`, `com.unity.modules.imageconversion` — decoding pictures (resolved from `package.json`)

## Architecture

```
ISaveStore                — read, write, delete, find, list, decode a picture; by SaveKey
├── DesktopSaveStore      — files under persistentDataPath, laid out as each SaveArea says
└── MemorySaveStore       — in memory, for tests and runs that must leave no trace

SaveKey = SaveArea + name    SaveArea is declared by the game: InFolder("Replays"), AtRoot("Settings", "settings")
SaveDocument              — one small text document over a store: read once, written whole after each change
SaveText                  — UTF-8 text over bytes (ReadTextAsync / WriteTextAsync)
```

Game code never builds a path. A console port writes its own `ISaveStore` (save containers per area) and registers it in place of `DesktopSaveStore`; nothing else changes.

## Core Concepts

**SaveArea** — What a piece of data is (settings, progress, replays). The game declares its areas once as static fields. An area also carries its desktop layout: `InFolder(name)` keeps it in a folder of its own, `AtRoot(name, stem)` keeps it as single files at the root that start with `stem.` — the layout most games already have for `settings.json`.

**SaveKey** — An area and a relative name in it, parts split by `/` (`s01/cover.jpg`). The constructor rejects `..`, `\`, `:`, empty parts, and names an area at the root does not own.

**Atomic writes** — `WriteAsync` replaces the old data whole or leaves it as it was. `DesktopSaveStore` writes `<file>.tmp` and then replaces the file; listings never show a `.tmp`.

## Quick Start

1. Declare the game's areas.
2. Register one store for the app.

```csharp
public static class CrewSaves
{
    public static readonly SaveArea Settings = SaveArea.AtRoot("Settings", "settings"); // settings.json
    public static readonly SaveArea Replays = SaveArea.InFolder("Replays");             // Replays/...
    public static readonly SaveArea Covers = SaveArea.InFolder("Covers");               // Covers/s01/box.jpg
}

builder.RegisterInstance<ISaveStore>(DesktopSaveStore.ForPlayer());
```

## Usage

### Reading and Writing

```csharp
var key = new SaveKey(CrewSaves.Replays, "mission03.replay");

await store.WriteAsync(key, replayBytes);           // whole or not at all
byte[] bytes = await store.ReadAsync(key);          // null when there is none
byte[] header = await store.ReadStartAsync(key, 64); // only the start, where the platform can
bool deleted = await store.DeleteAsync(key);

string json = await store.ReadTextAsync(settingsKey);  // UTF-8, any byte order mark read
await store.WriteTextAsync(settingsKey, JsonUtility.ToJson(settings));
```

Every call throws `SaveStoreException` when the platform fails (full disk, lost permission) and `ArgumentException` for a key the store cannot hold. `DesktopSaveStore` answers at once on the calling thread; call a large write from the thread pool.

### Listing

```csharp
SaveEntry? found = await store.FindAsync(key);              // size and write time, without reading
IReadOnlyList<SaveEntry> covers = await store.ListAsync(CrewSaves.Covers, "s01/");

foreach (SaveEntry entry in covers)
{
    Debug.Log($"{entry.Key.Leaf}: {entry.Size} bytes, {entry.Written:u}");
}
```

A listing is sorted by name (ordinal) and sees only its own area: `settings.json` and `progress.json` side by side at the root list apart.

### Pictures

```csharp
Texture2D cover = await store.ReadPictureAsync(new SaveKey(CrewSaves.Covers, "s01/box.jpg"));
if (cover != null)
{
    boxImage.image = cover; // mipped, sRGB, clamped, trilinear; the caller destroys it
}
```

Decoding belongs to the store because each platform has its own way off the main thread: `DesktopSaveStore` uses `UnityWebRequestTexture` on the file. Call it from the main thread. A missing or broken picture is `null`.

### Small Documents

`SaveDocument` keeps one text document — settings, progress, rebinds — over a store. Reads and writes take turns, the read first; a write takes its text when its turn comes, so the newest state is what lands.

```csharp
public sealed class CrewSettings
{
    private static readonly SaveKey Key = new(CrewSaves.Settings, "settings.json");
    private readonly SaveDocument _file;
    private SettingsData _data = new();

    public CrewSettings(ISaveStore store)
    {
        _file = new SaveDocument(store, Key);
        _file.ReadAsync(Take).Forget();
    }

    public void Save() => _file.WriteAsync(() => JsonUtility.ToJson(_data)).Forget();

    private void Take(string json)
    {
        if (json == null)
        {
            return; // none yet, or the store could not read it
        }

        try
        {
            _data = JsonUtility.FromJson<SettingsData>(json);
        }
        catch (ArgumentException)
        {
            _file.KeepBrokenAsync(json).Forget(); // kept as settings.broken.json
        }
    }
}
```

The store's failures are reported, not thrown: to `Debug.LogError` by default, or to the owner's log when one is passed.

```csharp
var file = new SaveDocument(store, Key, message => Log.Error(message));
string written = await file.WriteAsync(() => changed ? Text() : null); // null writes nothing; result null on failure
```

### Tests

```csharp
var store = new MemorySaveStore(); // keeps copies; every write is later than the one before
var settings = new CrewSettings(store);
```

## Design Decisions

- **Areas are values the game declares, not a package enum** — the package knows no game's data; the desktop layout rides on the area so `DesktopSaveStore` needs no game knowledge and old saves keep their place without migration.
- **The key checks its area's names** — `new SaveKey(Settings, "progress.json")` throws in every store, not only on desktop.
- **Async everywhere, though desktop finishes at once** — console save APIs are asynchronous; game code written against `UniTask` ports without changes.
- **`SaveDocument` takes an `Action<string>` for errors** — no dependency on a logging package; a game passes its own channel in one lambda.
- **No serializer** — stores keep bytes and text; JSON, binary or anything else stays the game's choice.
