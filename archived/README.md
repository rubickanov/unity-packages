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

Projects that use an archived package pin it to a commit (`#<hash>` in the git URL), so moving it here
does not break them as long as history is not rewritten.

## Reviving a package

Move it back into `packages/`, then re-add it to the sandbox manifest:

```bash
git mv archived/com.rubickanov.<name> packages/com.rubickanov.<name>
# then add the dependency + testables entry back to
# unity-project-pckgs/Packages/manifest.json
```
