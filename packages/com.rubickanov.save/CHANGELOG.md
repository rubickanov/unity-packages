# Changelog

## 1.1.0 — 2026-10-20

### Added

- `Rubickanov.Save.Data`, a new assembly beside the store (no reference to it): versioned documents of a game's own state.
  - `SaveNode` — a small tree (null, bool, number, string, array, object); an integer in the text that fits 64 bits stays a `long`.
  - `SaveJson` — canonical text (two-space indent, `\n`, ordinal key order, scalar arrays on one line, the fewest digits that read back exactly, `-0.0` kept) and a lenient reader (`JsonUtility` text, `\uXXXX`, NaN and Infinity read as null with a note, errors with line and column).
  - `SaveWriter` / `SaveReader` — values by key over a copy of the base tree, so keys and map-entry fields a build does not know survive its writes; fallbacks and notes with key paths for missing or wrong values.
  - `SaveFormat` — the `$kind` / `$version` / `$build` header; `Read` gives `Ok`, `Broken`, `WrongKind` or `Newer` (a newer format is refused, never read).
  - `SaveMigrations` — a chain of steps over the tree: `Rename`, `Move`, `Remove`, `Set`, `Edit`, `Each`, `Run`.
  - `SaveFixtures.Check` (editor) — golden files: every released version walks the chain and must equal its expected file.
- Golden files of the example till document in `Tests/Fixtures/till/`.
- `Rubickanov.Save.World`, a new assembly over the store: world slots.
  - `SaveSlots` — slots in an area of their own folder: `ListAsync`, `FindAsync` (read only), `CreateAsync` (8-hex id), `RenameAsync`, `DeleteAsync`, `Open`.
  - `SaveWorld` — `LoadAsync` (newest whole generation, fallback past broken ones with copies aside, whole-slot refusal of newer data, restore in the game's order), `Save`, `SaveAsync`, `SaveNow`, `Tick`, `Close`.
  - `ISaveSection` / `SaveSection.Of`, `ISaveSectionCheck` with `SaveCheck` (Ok / Broken / Newer), `ISaveSectionKinds` (kinds with versions listed in the manifest), `SlotInfo`, `SaveReport`.
  - Generations: section files, then the manifest (`$kind` `slot`, v1) as the commit, with length and CRC-32 of every file; unchanged text names the old file; 3 whole generations kept.
  - Attachments: `SaveWriter.Attach` / `SaveReader.Attachment`, binary files beside a section (`SaveAttachments`).
- Pieces of the world in `Rubickanov.Save.World` (`World/Pieces/`).
  - `SaveId` — a guid given once to a thing, 32 hex digits as text (`New`, `TryParse`, `IsEmpty`).
  - `ISavePiece` (`SaveId`, `Kind`, `Capture`), `ISavePieceLinks` (`Link`, a second pass after every piece is spawned).
  - `PieceKind` — a kind's factory and its own migrations.
  - `SavePieces` — the `pieces` section: records `{"$v", "kind", …}` by SaveId; `Add`, `Track`, `Forget`, `Find`, `Resolve<T>`, `Count`, `Orphans`, `OrphanRecords`. The load's check migrates records in place (a failing step makes the generation broken, a newer record refuses the slot); kinds the build cannot spawn are kept and written back unchanged; the manifest's `kinds` lists every record's kind, orphans included.
  - `PieceFixtures.Check` (editor) — golden files of a kind; the example `kite` in `Tests/Fixtures/pieces/kite/`.
- `FaultySaveStore` in the tests; golden file of the slot manifest in `Tests/Fixtures/slot/`.

### Unchanged

- The store API (`ISaveStore`, `SaveArea`, `SaveKey`, `DesktopSaveStore`, `MemorySaveStore`, `SaveDocument`, `SaveText`).

## 1.0.0

- The store: game-declared areas and keys, async calls, atomic writes, desktop and memory stores, `SaveDocument`, pictures.
