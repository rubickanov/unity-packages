# CLAUDE.md

Guidance for Claude Code when working in this repo.

## What this repo is

Monorepo of personal reusable Unity packages under `packages/com.rubickanov.*`. Each package is independently installable via UPM git URL (see root `README.md`).

### Packages

Versions: `app` `1.0.0`, `audio` `3.5.4`, `log` `1.3.0`, `ui` `4.1.0`, `devconsole` `5.1.0`, `save` `1.3.0`, `input` `1.2.0`, `kinematics` `1.0.0`, `character` `5.3.0`, `settings` `1.2.0`, `report` `1.0.0`:

| Package | Purpose |
|---|---|
| `app` | No-DI glue both games share: `AppScope<TSelf, TApp, TScene>` and `SceneScope<TApp, TScene>` the game subclasses over its own services, `Lifecycle` (initialize, time tick, early tick, start, tick, fixed, pre-physics and physics ticks, pre-late and late ticks; `Own` disposes last taken first; first exception per step and kind logged; a profiler marker per step) and `FrameLoop` putting those places into the player loop. Game-only frame work (SPLATDOWN's `Physics.SyncTransforms`) is steps the game registers. Only `com.rubickanov.log`. |
| `audio` | Audio service: SFX pooling, music, AudioMixer volume; 3D sound kinds (distance, air, width, reverb send, head shadow, occlusion) through `SoundSpace`, HRTF on kinds marked binaural through Steam Audio's spatializer (`Binaural`, native libraries in the package), a lookahead true-peak `OutputLimiter` on the listener, `MixerKit` for mixer layouts in the editor, `ListenerCapture` for checks. |
| `devconsole` | In-game dev console: one IMGUI window the package creates, `[ConsoleCommand]` methods registered by a source generator (no reflection, safe under code stripping), autocomplete, chains, aliases, bindings and history in plain files, `autoexec.cfg` and `-command` run on the first frame, optional `log` commands. |
| `character` | Character body of beach (leonid carries an embedded copy, 2.2.0, since 2026-10-16): motor over KCC, animator on Animancer, ragdoll + get-up, IK (feet, head look, turn lean, seat holds and item grips, climb), configs and hooks; an optional module `Rubickanov.Characters.Netcode` (compiled only with NGO, versionDefines) replicates it with game parts and glue. KCC (`KCC`) and Animancer (`Kybernetik.Animancer`) are Asset Store code: referenced by asmdef name only, never copied here; the game provides them, `com.rubickanov.log` and `com.rubickanov.kinematics` (its IK, contact, pin and spring maths; floats meet the package's doubles in `Rubickanov.Rig.KinematicsInterop`). Not in `unity-project-pckgs` (would not compile there); its tests (`Rubickanov.Rig.Tests`, `Rubickanov.Characters.Netcode.Tests`) run from a game linked `local`. |
| `input` | What Input System leaves to each game, on its own action types: the device in hand (Steam Input's duplicate pad included), key names for the player's layout and pad family, an action's keys for hints, rebind slots with conflicts, swaps and JSON (kept in the save store by an optional assembly), maps blocked by reason, local players seated by a press with their own input copy (Steam Input copies kept out), devices fed from code for Remote Play Together guests. |
| `kinematics` | Deterministic pose maths on `double` (SPLATDOWN's Sinew body; a `float3` game converts at its boundary): `DetMath` (sin, atan2, exp… from + − × ÷ sqrt), quaternions, two-bone IK, foot contact and pinning, pelvis tilt, head look, turn lean, reach, springs; same bits under Mono, IL2CPP and Burst, golden hash in its tests. Only `com.unity.mathematics` (Burst for the tests). |
| `log` | Named log channels over Unity's log: per-channel levels (`-log Course=Verbose`), interpolated messages built only when written, Verbose compiled out of release, a speaker scope (`[Host]`) before every message. No dependencies. |
| `report` | Bug reports over the Sentry Unity SDK (ex beach `Assets/Scripts/Report`): `Reporter` starts the SDK itself (`ReportGate` keeps its auto-start off in play), quietly off without a DSN; each kind of error once a session under a cap (`Fingerprint`, `OnceASession`), the player's folders (user name) scrubbed to `~` from the log tail, breadcrumbs, error lines and exception texts (`PathScrub`), hangs dropped while `Busy`, feedback with the moment's screenshot (`Moment`) linked to the last problem, frame rate as metrics (`FrameSampler`), a random `InstallId` in the save store as the only user. No IP, no machine name. The game provides `io.sentry.unity` 4.11.0 and draws its own window; `Rubickanov.Report.Editor.SentryOptionsSetup` for its builder. Not in `unity-project-pckgs` (no Sentry there); tests run from a game linked `local`. |
| `save` | One store for the player's data: game-declared areas and names instead of paths, async calls (console-ready), atomic writes; files on desktop in each area's layout, memory for tests, small text documents read once and written in turn, pictures decoded off the main thread; `Rubickanov.Save.Data`: versioned documents of game state (own tree, canonical JSON, migration chain over the tree, unknown fields kept, newer formats refused, golden fixtures); `Rubickanov.Save.World`: world slots committed by generation (manifest last), fallback past broken generations, saves captured on the main thread and written on the pool; pieces of the world by Kind and SaveId (spawned by factory, linked in a second pass, unknown kinds kept as orphans). |
| `ui` | UI Toolkit framework (no UGUI or TextMeshPro): views with view models on layers, a screen history, popups (with registered views as content), unscaled-time animations, pointer capture, back stack and keyboard/pad menu navigation fed by the game, UXML catalog, a stat panel (version, FPS, any number the game reads) coloured by limits. |

Frozen packages live in `archived/` with the reason for each (`archived/README.md`); they are not in the sandbox or the docs.

## Repo layout

```
unity-packages/
├── packages/                    # ← package source lives HERE
│   └── com.rubickanov.<name>/
│       ├── Runtime/
│       ├── Editor/              (if present)
│       ├── Tests/               (if present, gated by UNITY_INCLUDE_TESTS)
│       ├── package.json
│       └── README.md
├── unity-project-pckgs/         # Unity dev sandbox — edit packages live here
│   ├── Assets/                  (mostly empty — just NuGet config)
│   ├── Packages/manifest.json   (references every package via file:../../packages/...)
│   └── *.csproj                 (GENERATED — never edit)
├── docs/
│   ├── generate.sh              # regenerates DocFX site from package READMEs
│   └── _site/                   (build output)
├── link.sh                      # switches a consumer project's manifest local ↔ remote
├── README.md
└── README_STANDARD.md           # style guide for package READMEs
```

### unity-project-pckgs is the dev sandbox

It's a Unity project whose `Packages/manifest.json` pulls every package as `file:../../packages/com.rubickanov.<name>`. All packages are listed in the manifest's `testables` array so tests show up in the Unity Test Runner. **This is where Unity editor state, `Library/`, and generated `.csproj` files live** — none of that belongs in the package folders.

The actual working directory when Claude starts is `unity-project-pckgs/`, so package source is one level up at `../packages/com.rubickanov.<name>/`.

## Working rules

- **Edit packages under `packages/com.rubickanov.<name>/`.** Never touch the generated `*.csproj` files in `unity-project-pckgs/` — they're regenerated by Unity.
- **Don't commit `unity-project-pckgs/Library/`, `Temp/`, `Logs/`, `obj/`, `UserSettings/`.** Those are Unity editor state.
- **READMEs follow `README_STANDARD.md`.** English prose, code examples over descriptions, no marketing fluff. Code comments may be Russian.
- **Package tests are gated by `UNITY_INCLUDE_TESTS` + `includePlatforms: [Editor]`.** They only compile when the consuming project has the package as a local `file:` reference and lists it in `testables`.
- **`link.sh <project> [status|local|pin <sha>|remote] [package...]`** switches a *consumer* project's manifest between this checkout (`local`: absolute `file:` path + `testables`) and the https git URL pinned at a commit (`pin <sha>`, what games commit). Don't run it against `unity-project-pckgs` itself — that sandbox stays local.
- **Fixing a package a game uses** (e.g. `character` in beach and leonid): `./link.sh <game> local character` → fix here and check in the game (both games if the behaviour is shared) → commit + push here → `./link.sh <game> pin <sha> character` in each game and commit its `Packages/manifest.json` + `packages-lock.json` there.
- **`docs/generate.sh`** regenerates the DocFX site. `docs/docfx.json`, `docs/index.md`, `docs/toc.yml`, `docs/guides/toc.yml`, `docs/api/index.md` are regenerated on every run — don't hand-edit them.
- **Bumping a package version:** edit `packages/com.rubickanov.<name>/package.json`.
- **No LINQ in runtime hot paths.** `System.Linq` is banned in `Runtime/` code that can fire per-tick / per-frame / per-entity — boxed enumerators and closure captures generate GC pressure Unity's main loop can't afford. Use `foreach`, hand-rolled loops, pooled `List<T>`, or a `struct Enumerator GetEnumerator()` pattern for public collection types.
  - **Cold-path exception:** LINQ is acceptable in scan/reflection/spawn-time code that (1) runs at most once per type per session and (2) caches its result. Examples: `ReplicationScanner` building field metadata on first aspect discovery, reflection-driven factory setup. The allocation cost amortizes to zero over a game session and code clarity wins.
  - **Tests may use LINQ freely.** Don't strip `.ToList()` / `.Any()` / `.Where()` out of test files during refactors.

## Tests

- NUnit, AAA structure (Arrange / Act / Assert). No phase-label comments — blank line separation between phases is enough.
- Name: `Method_Scenario_ExpectedBehavior` (e.g. `Scan_FieldsDeclaredInReverseAlphabeticalOrder_ReturnedAlphabetically`).
- One behavior per test. Multiple related asserts in a single test are fine — don't split artificially.
- Assertion messages only when the failure cause isn't obvious from the assert itself.
- Prefer per-test fixtures (nested classes, fresh instances) over shared state when the SUT has static/global state that could leak between tests.