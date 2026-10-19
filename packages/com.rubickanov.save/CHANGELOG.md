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

### Unchanged

- The store API (`ISaveStore`, `SaveArea`, `SaveKey`, `DesktopSaveStore`, `MemorySaveStore`, `SaveDocument`, `SaveText`).

## 1.0.0

- The store: game-declared areas and keys, async calls, atomic writes, desktop and memory stores, `SaveDocument`, pictures.
