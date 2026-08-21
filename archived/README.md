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

## Reviving a package

Move it back into `packages/`, then re-add it to the sandbox manifest:

```bash
git mv archived/com.rubickanov.<name> packages/com.rubickanov.<name>
# then add the dependency + testables entry back to
# unity-project-pckgs/Packages/manifest.json
```
