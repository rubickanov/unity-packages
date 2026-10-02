# Archived packages

Packages here are **frozen**: not maintained, not part of the docs site, and not
referenced by the `unity-project-pckgs` sandbox. Their source and git history are
kept for reference and in case they are ever revived.

They are intentionally excluded from `packages/` so that `docs/generate.sh` (which
scans `packages/` only) and the sandbox `Packages/manifest.json` skip them
automatically.

| Package | Why archived |
|---|---|
| `com.rubickanov.character-motor` | Superseded by **Kinematic Character Controller (KCC)** — the de-facto standard for Unity character controllers. KCC is a proprietary Asset Store asset (cannot be legally forked/relicensed), and a clean-room reimplementation to reach KCC-grade movement is months of work in an already-crowded niche. The package's only genuine edge was server-authoritative netcode prediction, which is not currently needed. See `com.rubickanov.character-motor/KCC-INSPIRED.md` for the full analysis. |
| `com.rubickanov.acs`, `acs.debug`, `acs.persistence`, `acs.reactive` | The aspect/component architecture the games have left for VContainer + R3 + plain MonoBehaviours (cosmic-crew removed its stack in 2026-09). `acs.reactive` is one `CombineLatest(...).ToReadOnlyReactiveProperty()` in R3. Archived 2026-10-02. |
| `com.rubickanov.acs.netcode` | Replication for ACS over Netcode for GameObjects; the games moved to their own network layer. Its sandbox experiment (scene, prefab, scripts) is in `Experiments~`. Its half-float and smallest-three codecs are worth reading if a network layer needs quantization. Archived 2026-10-02. |
| `com.rubickanov.behaviortree` | Unity's free `com.unity.behavior` does all of it and more (parallel, waits, reactive aborts, debugger); ours had Sequence, Selector, Inverter, Cooldown and Subtree only. Archived 2026-10-02. |
| `com.rubickanov.gameplaytags`, `com.rubickanov.gas` | Unreal-style tags and an attribute/buff system (no abilities or cues); no RPG is planned. Revive both together if one is. Archived 2026-10-02. |
| `com.rubickanov.storage` | A PlayerPrefs-style key/value store; wipeout's `ISaveStore` (whole files by area, atomic writes, async for consoles) is the base for a future shared save package. Archived 2026-10-02. |
| `com.rubickanov.loading`, `com.rubickanov.ui.loading` | A loading-step pipeline; wipeout's `SceneFlow` (preload, curtain, minimum time, scene check, failure screen) is the base for a future scene-flow package. Archived 2026-10-02. |
| `com.rubickanov.localization`, `com.rubickanov.ui.localization` | Unity 6 Localization binds `LocalizedString` to UI Toolkit itself; the key-constant generator is the part worth reviving. Archived 2026-10-02. |
| `com.rubickanov.logging` | Replaced by `com.rubickanov.log` in every game. Archived 2026-10-02. |
| `com.rubickanov.steam-transport`, `com.rubickanov.devconsole.netcode` | Prototypes for Netcode for GameObjects, which no game uses now. Archived 2026-10-02. |
| `com.rubickanov.devconsole.config` | About ten lines of `RegisterParser` in game code. Archived 2026-10-02. |
| `com.rubickanov.ui.animations` | LitMotion fade, scale, slide and composite on scaled time, so at `Time.timeScale` 0 a pause menu's hide never finished; and the looks a game wants are its own (wipeout's wipe and squeeze). `com.rubickanov.ui` 4.0.0 has the unscaled `Tween.Run` they are built on, `AnimationTarget` for animating an inner element, and a plain `ViewAnimations.Fade`. Archived 2026-10-02. |
| `com.rubickanov.config` | Addressables-backed config loading with catalog refresh, built for remote content updates no game has; the games keep their configs as assets handed out by their scopes or `Resources`. It also leaked every Addressables handle; `ISSUES.md` beside it lists what to fix before a revival. Archived 2026-10-02. |
| `com.rubickanov.utils` | A grab bag no game used: Unity's own `UnityEngine.Pool.ObjectPool` covers pooling (ours released delayed items on scaled time, so never during a pause), the `[Description]` inspector replaced the inspector of every MonoBehaviour in a project with IMGUI, and the ring buffer's look-back promise for wrapped `uint` indices held only for power-of-two capacities. `DeterministicRandom` (a murmur3-finalizer hash RNG) is one file to copy where a game needs it. Archived 2026-10-02. |
| `com.rubickanov.codegen` | Editor generators of Unity constants (scenes, layers, tags, shader IDs, UXML names, ...) that know nothing of the game; no game used them, and a game's own constants say more. Worth reviving is the core, for a game's own generator from its data (localization keys, commentator lines): writing a file only when its text changes, so nothing recompiles needlessly, and turning strings into unique C# identifiers. That sanitizer still lets a member share its enclosing class's name (CS0542). Code-from-code work, such as console command registration without reflection, belongs in a Roslyn source generator instead. Archived 2026-10-02. |
| `com.rubickanov.statemachine` | A generic FSM no game used: the games keep their states as an enum and a switch in the class that owns them (wipeout's `CharacterAnimator`, `CourseRun`, `ReplayPlayer`), where a transition reads the phase it left and the hit that caused it. Too thin to earn a library (no data passed into a state, no guards, no debug view) and more than the thirty lines a game writes in place. The async machine had traps: `SetStateAsync` during another transition returned at once with the change only queued, and `Update` ticked a state still exiting or not yet entered. In the sync one a `SetState` back to the current key during a transition was dropped while the earlier queued key still ran, and `Stop()` from `OnEnter` still fired `StateChanged`. Archived 2026-10-02. |

Projects that use an archived package pin it to a commit (`#<hash>` in the git URL), so moving it here
does not break them as long as history is not rewritten.

## Reviving a package

Move it back into `packages/`, then re-add it to the sandbox manifest:

```bash
git mv archived/com.rubickanov.<name> packages/com.rubickanov.<name>
# then add the dependency + testables entry back to
# unity-project-pckgs/Packages/manifest.json
```
