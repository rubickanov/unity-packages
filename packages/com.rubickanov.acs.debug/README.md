# ACS Debug

Editor window that lists every live entity in the active `World` and shows its aspect fields with
live values. Extension for [ACS](../com.rubickanov.acs/).

## Dependencies

> `R3` and `ObservableCollections` come from NuGet, not from UPM — UPM will not pull them in for you. See [Third-party dependencies](https://github.com/rubickanov-org/unity-packages#third-party-dependencies).

- `com.rubickanov.acs` — reads `World.Current` and reuses `RuntimeAspectDrawer` for field rendering
- `R3` — pulled in transitively for reactive field inspection

## Quick Start

1. Enter Play Mode with a `MonoWorld` in the scene.
2. Open `Window ▸ ACS ▸ Debugger`.
3. Pick an entity from the left list — its aspects and live field values appear on the right.

## Usage

The left pane lists every entity registered with `World.Current` — `MonoEntity` (shown by
GameObject name), pure-C# `Entity`, and the `World` itself (`World (global)`). Each row shows the
entity label and its aspect count. The filter box matches against the entity label and aspect type
names.

The right pane renders the selected entity through the same `RuntimeAspectDrawer` used by the
`MonoEntity` inspector, so reactive properties, `Subject` signals, and `ObservableCollections`
fields display with the same formatting and flash-on-change highlight. Selection keys off the
stable `EntityId`, so a despawned entity simply drops out of the list.

## Notes

- **Editor-only.** The window lives in an Editor assembly and contributes no code to a build.
- **Play Mode only.** Outside Play Mode there are no live aspect instances to read.
- **World-registered entities only.** A standalone `Entity` created without a `World` is not in
  any registry and will not appear; pass a `World` to its constructor to make it visible.
- **`[Computed]` fields** (from `acs.reactive`) render via their `ToString()` — the current value
  is shown, without the dedicated reactive formatting.
- Network traffic, subscription counts, and dirty-state per field (sketched in the ACS ideas doc)
  need netcode/R3 hooks and are not part of this version.
