# Save

One store for everything a game keeps of the player's, named by game-declared area and name instead of a path: async like console saves, every write whole or not at all, swapped as one piece when the game moves to another platform; beside it, versioned documents of a game's own state that every later build can still read, and world slots that survive a crash at any moment of a save.

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

Rubickanov.Save.Data (no store reference):
text ─SaveFormat.Read─▶ SaveJson ─▶ SaveNode ─header check, SaveMigrations v1→…→current─▶ SaveReader ─▶ system
system ─SaveWriter over a copy of the base─▶ SaveNode (next base) ─SaveFormat.Write─▶ canonical JSON text

Rubickanov.Save.World (over a store, in an area of its own folder):
SaveSlots                 — list, find, create, rename, delete slots; Open(sections in restore order) ─▶ SaveWorld
SaveWorld                 — LoadAsync once; Save: capture on the main thread ─▶ text + CRC + files on the pool
Saves/<slot>/             — <section>.<N>.json files, then manifest.<N>.json (the commit); newest whole N is the slot
SavePieces                — section "pieces": things by SaveId, spawned by Kind, linked in a second pass, orphans kept
```

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.Save.Runtime** | Yes | The store: areas, keys, desktop and memory stores, `SaveDocument`, pictures |
| **Rubickanov.Save.Data** | Yes (vectors, colours) | Versioned documents: `SaveNode`, `SaveJson`, `SaveWriter`/`SaveReader`, `SaveFormat`, `SaveMigrations`, `SaveFixtures` (editor only) |
| **Rubickanov.Save.World** | Yes | World slots: `SaveSlots`, `SaveWorld`, `ISaveSection`/`SaveSection`, `ISaveSectionCheck`, `ISaveSectionKinds`, `SlotInfo`, `SaveReport`; pieces of the world (`Pieces/`): `SaveId`, `ISavePiece`, `ISavePieceLinks`, `PieceKind`, `SavePieces`, `PieceFixtures` (editor only) |
| **Rubickanov.Save.Tests** | Editor | Store, document and slot tests, golden files in `Tests/Fixtures/`, `FaultySaveStore` |

Game code never builds a path. A console port writes its own `ISaveStore` (save containers per area) and registers it in place of `DesktopSaveStore`; nothing else changes.

## Core Concepts

**SaveArea** — What a piece of data is (settings, progress, replays). The game declares its areas once as static fields. An area also carries its desktop layout: `InFolder(name)` keeps it in a folder of its own, `AtRoot(name, stem)` keeps it as single files at the root that start with `stem.` — the layout most games already have for `settings.json`.

**SaveKey** — An area and a relative name in it, parts split by `/` (`s01/cover.jpg`). The constructor rejects `..`, `\`, `:`, empty parts, and names an area at the root does not own.

**Document** — An object whose header says what it is (`$kind`), its format version (`$version`) and the build that wrote it (`$build`); the other keys are data. Kept as a `SaveNode` tree: an integer in the text (`-` and digits only) that fits 64 bits stays a `long`, any other number is a `double`.

**Canonical text** — `SaveJson.Write` gives the same bytes for the same tree: two-space indent, `\n`, keys in ordinal order, arrays of scalars on one line, each number in the fewest digits that read back exactly (`0.1f` is `0.1`, `-0.0` keeps its sign). Diffs read well; capture → restore → capture changes nothing.

**Migration chain** — The classes that wrote an old version are gone, so a step v-1 → v works on the tree: rename, move, remove, set, or own code over a node. Any released version walks the chain to the current one; a file of a newer version is refused (`Newer`), never read and never written over.

**Base** — A system writes over a copy of what was read (its base), not over an empty object: keys it does not write stay, so a field a newer build added survives an older build's save. The written tree is the next base, so what the system removed stays removed.

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

### Versioned Documents

A format names the document and its migration chain; a system reads and writes its values by key.

```csharp
// v1: whole dollars; v2: cents under a new name; v3: tips beside the cash.
static readonly SaveFormat TillFormat = new("till", new SaveMigrations()
    .Step(2, m => m.Rename("money", "cash").Edit("cash", n => SaveNode.Of(n.Long * 100)))
    .Step(3, m => m.Move("cash", "till/cash").Set("till/tips", SaveNode.Of(0))));

private SaveNode _base; // what was read; after each save, what was written

public void Load(string text)
{
    SaveRead read = TillFormat.Read(text);
    switch (read.Status)
    {
        case SaveReadStatus.Ok:
            _base = read.Data; // already v3
            var notes = new List<string>();
            SaveReader till = SaveReader.Of(read.Data, notes).Object("till");
            Cash = till.Long("cash");
            Tips = till.Long("tips");
            break;
        case SaveReadStatus.Newer:
            _refused = true; // "made by 0.9.0 with till v4; this build knows v3": do not write over it
            break;
        default:
            _file.KeepBrokenAsync(text).Forget(); // Broken or WrongKind: kept aside
            break;
    }
}

public string Save()
{
    SaveWriter w = SaveWriter.Over(_base);
    SaveWriter till = w.Object("till");     // written into the old object: unknown keys stay
    till.Long("cash", Cash);
    till.Long("tips", Tips);
    _base = w.Tree;
    return TillFormat.Write(w.Tree, Application.version);
}
```

```json
{
  "$build": "0.3.0",
  "$kind": "till",
  "$version": 3,
  "till": {
    "cash": 12000,
    "tips": 250
  }
}
```

The writer has `Int`, `Long`, `Float`, `Double`, `String`, `Bool`, `Enum` (by name), `Vector2`, `Vector3`, `Quaternion`, `Color`, `Time` (UTC text), `Object`, `List`, `Map`, `Node` and `Remove`; the reader the same with fallbacks. A missing key gives the fallback quietly; a value of the wrong kind gives the fallback and a note (`clock/day: a number expected, "three" found`).

```csharp
SaveMapWriter kept = w.Map("kept");            // entries by id: one not written is gone (the guest left)...
kept.Entry(guest.Id).String("name", guest.Name); // ...one written keeps its fields this build does not know

SaveListWriter links = w.List("links");        // a list is written whole
links.Add().String("to", "jetski");

foreach (SaveEntryReader e in r.Map("kept"))
{
    Spawn(e.Id, e.Value.String("name"));
}
```

Keep records that live longer than one save (people, players, things) in a map by a stable id rather than a list. A field goes away for good only by a `Remove` step with a new version.

### Migrations

```csharp
new SaveMigrations()
    .Step(2, m => m
        .Rename("money", "cash")                          // in the same object
        .Move("cash", "till/cash")                        // anywhere; objects on the way are made
        .Remove("legacy")
        .Set("till/tips", SaveNode.Of(0))
        .Edit("cash", n => SaveNode.Of(n.Long * 100))     // own code over a node; null removes it
        .Each("kept", e => e.Rename("hp", "health"))      // the same in every entry of a map or list
        .Run(root => root["seen"] = SaveNode.Of(true)));  // own code over the whole data
```

An operation on a path that is not there does nothing. A step that throws makes the read `Broken` for this build; the text read is not changed.

### Golden Files

Every released version of a document stays in the tests as a file forever: `v<N>.json` is what a build of version N really wrote, `v<N>.expected.json` what it must become now (with `$build` `fixture`). The current version's file must come out as itself, byte for byte.

```
Tests/Fixtures/till/v1.json  v1.expected.json  v2.json  v2.expected.json  v3.json
```

```csharp
[Test]
public void Till_EveryReleasedVersion_ReadsAsExpected()
{
    Assert.That(SaveFixtures.Check(Path.GetFullPath("Packages/com.game/Tests/Fixtures/till"), TillFormat), Is.Empty);
}
```

A missing expected file is written beside its version and reported: review the diff and commit it. When v4 comes, every expected file changes (that diff is the review of the migration), `v3.json` gets its own expected file, and a new `v4.json` is taken from the game. A released `v<N>.json` is never edited.

### World Slots

A slot is one world (one beach): a folder of generations. Each system declares a section; the game lists them once, in the order they restore.

```csharp
var slots = new SaveSlots(store, SaveArea.InFolder("Saves"), BuildVersion.Text, log.Error); // keeps 3 generations
SaveWorld world = slots.Open(new[]
{
    SaveSection.Of("clock", null, w => w.Int("day", Day), r => Day = r.Int("day")),
    SaveSection.Of("wallet", null, w => w.Long("cash", Cash), r => Cash = r.Long("cash")),
}, describe: w => w.Int("day", Day)); // what a slot list shows, kept in the manifest

SlotInfo beach = (await slots.ListAsync()).FirstOrDefault() ?? await slots.CreateAsync("Hotel beach");
SaveReport report = await world.LoadAsync(beach.Id); // restores every section the slot has, in that order
if (report.Refused != null) { /* made by a newer build: neither loaded nor written */ }

world.Save();              // capture now (main thread), write on the pool; call world.Tick() every frame
await world.SaveAsync();   // the same, true once the generation is in (completes in Tick)
world.SaveNow();           // on quit: waits for the write in flight (5 s at most), writes on this thread
```

```
Saves/3f9a2c71/
  manifest.000041.json  manifest.000042.json  manifest.000043.json   ← the newest whole one is the slot
  clock.000043.json  clock.000042.json
  wallet.000040.json         ← unchanged since 40: newer manifests name this file
  radio.000012.json          ← a section this build does not know: carried, never rewritten
  manifest.000039.broken.json
```

- **Commit by generation.** A save writes new files `<section>.<N>.json` (unchanged text names the old file), then `manifest.<N>.json` last: before that write generation N does not exist, so a crash leaves N-1 whole. N is one more than the highest number in any file of the slot, so a number is never reused.
- **Fallback.** A load takes the newest manifest that reads and whose every file has its length and CRC-32, whose known sections parse and migrate and pass their `ISaveSectionCheck`; a broken one is copied to `*.broken.json`, told in `SaveReport.Notes` and the log, and the previous generation loads. A store failure while reading is an error, not a broken file.
- **Newer is refused.** A manifest, a known section or a known kind newer than the build refuses the whole slot: no restore, and saves, `RenameAsync` and `DeleteAsync` leave every byte as it was. Unknown sections and unknown fields are kept.
- **Sweep.** After a commit the 3 newest whole manifests stay; older ones and those a load found broken go, then every file no kept manifest names. Copies of broken files stay.
- **Saves in flight.** One write per slot at a time (renames queue behind it); a save asked meanwhile is captured at once and waits, a newer one replaces it.
- **Attachments.** `w.Attach("chunk-12", bytes)` in `Capture` keeps a file `<section>.chunk-12.<N>.bin` beside the section; `r.Attachment("chunk-12")` gives it back in `Restore`.
- A crash during a desktop write can leave a `*.tmp` beside the file: the store never lists it and the slot never reads it, so it is harmless.

### Pieces of the World

Things the world holds — sunbeds, boats, a rope clipped to one — are one section, `SavePieces` (`pieces`). Each thing has a `Kind` (a stable string of the game's catalog) and a `SaveId` given once when it is first created; the game adds a `PieceKind` per kind with its factory and its own migrations.

```csharp
var pieces = new SavePieces(log.Error);
pieces.Add(new PieceKind("boat", (id, r) => fleet.Spawn(id, r.Vector3("p"), r.Quaternion("r"))));
pieces.Add(new PieceKind("rope", (id, r) => ropes.Spawn(id, r.Vector3("p")),
    new SaveMigrations().Step(2, s => s.Rename("to", "end")))); // the rope's own v2
SaveWorld world = slots.Open(new ISaveSection[] { clock, pieces, wallet });

sealed class Rope : ISavePiece, ISavePieceLinks
{
    public SaveId SaveId { get; }               // SaveId.New() when first made, the saved one when loaded
    public string Kind => "rope";
    SaveId _end;                                // kept as read, so a link to a piece that did not load survives

    public void Capture(SaveWriter w)
    {
        w.Vector3("p", transform.position);
        if (!_end.IsEmpty) w.String("end", _end.ToString());
    }

    public void Link(SaveReader r, SavePieces pieces) // after every piece is spawned
    {
        SaveId.TryParse(r.String("end"), out _end);
        Boat boat = pieces.Resolve<Boat>(r, "end"); // null and a note when it is not in the world
        if (boat != null) ClipTo(boat);
    }
}

pieces.Track(rope);   // a thing appears (made by default or by the player); loaded ones are tracked already
pieces.Forget(rope);  // it is gone: the next save drops it
```

```json
{ "pieces": {
    "3f9a…": { "$v": 1, "kind": "boat", "p": [12, 0, 4], "r": [0, 0, 0, 1] },
    "8c01…": { "$v": 2, "kind": "rope", "end": "3f9a…", "p": [12, 0, 6] },
    "d2e7…": { "$v": 1, "kind": "kite", "length": 4.5 } } }   ← a kind this build does not know: an orphan
```

- **Check before any restore.** While the slot loads, every record of a known kind walks its kind's chain in place; a step that throws makes the generation broken (the previous one loads, every section with it), a record newer than its kind refuses the slot.
- **Spawn, then link.** Restore spawns each record through its kind's factory, then calls `Link` on every piece that has it, so a rope finds a boat saved after it. A link to a piece that is not there is a note in `SaveReport.Notes`.
- **Orphans.** A kind the build does not know, or a factory that returns null, keeps the record raw (`Orphans`, `OrphanRecords`) and writes it back unchanged; a build that knows the kind spawns it again.
- **Kinds in the manifest.** A save lists each kind with its highest version, orphans too: a build that knows `kite` only at v1 refuses a slot that a kite-less build carried a `kite` v2 through.
- **Golden files per kind.** `PieceFixtures.Check(folder, kind)` walks `v<N>.json` (one record each) through the kind's chain and compares with `v<N>.expected.json`, as `SaveFixtures` does for documents.

### Tests

```csharp
var store = new MemorySaveStore(); // keeps copies; every write is later than the one before
var settings = new CrewSettings(store);
```

`FaultySaveStore` (in the package's tests) wraps a store and dies like a process at the k-th write: every write and delete from it on throws. It can also sleep on each write and corrupt a file.

## Design Decisions

- **Areas are values the game declares, not a package enum** — the package knows no game's data; the desktop layout rides on the area so `DesktopSaveStore` needs no game knowledge and old saves keep their place without migration.
- **The key checks its area's names** — `new SaveKey(Settings, "progress.json")` throws in every store, not only on desktop.
- **Async everywhere, though desktop finishes at once** — console save APIs are asynchronous; game code written against `UniTask` ports without changes.
- **`SaveDocument` takes an `Action<string>` for errors** — no dependency on a logging package; a game passes its own channel in one lambda.
- **No serializer in the store** — stores keep bytes and text; JSON, binary or anything else stays the game's choice. `Rubickanov.Save.Data` is one such choice, in its own assembly, with no reference to the store.
- **Explicit code by keys, no reflection or attributes** — a saved field is a line a system writes and reads; renaming a C# field changes no file, and code stripping cannot break it.
- **Migrations over the tree, not over classes** — a class an old version was read into does not survive refactors; the tree does.
- **A manifest per generation, written last** — the same write-new-then-switch commit databases use; no journal, no rename of folders, and the previous generations are the backups.
- **Capture on the main thread, the rest off it, never back** — a capture is reading fields into a tree; text, checksums and files cost the pool. Nothing in the write waits for the main thread, so `SaveNow` may block it on quit without a deadlock.
- **Pieces by kind and SaveId, not by scene path or network id** — a scene path changes with the scene and a network id with the session; a guid given once and a catalog string survive both. References between pieces are SaveIds resolved after every piece is spawned, so the order of records never matters.
- **Our own JSON** — no new dependency (no Newtonsoft); exact 64-bit integers, a canonical text that diffs well, and lenience for `JsonUtility` text (NaN and Infinity read as null with a note).
