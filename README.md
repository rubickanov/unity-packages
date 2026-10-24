# Unity Packages

Shared Unity packages.

## Installation

Unity Package Manager → Add package from git URL:

```
git+ssh://git@github.com/rubickanov/unity-packages.git?path=packages/com.rubickanov.<NAME>
```

Or HTTPS:

```
https://github.com/rubickanov/unity-packages.git?path=packages/com.rubickanov.<NAME>
```

Replace `<NAME>` with a package folder under `packages/` (e.g. `ui`).

Pin to a specific commit by appending `#<hash>`:

```
git+ssh://git@github.com/rubickanov/unity-packages.git?path=packages/com.rubickanov.ui#a1b2c3d
```

## Third-party dependencies

Each package's own README lists what it needs under **Dependencies**. Sibling
`com.rubickanov.*` packages and registry packages (`com.unity.*`) resolve
automatically from `package.json`. The rest do not — UPM's `dependencies` field
only resolves from a registry, so anything installed from a git URL or from NuGet
has to be present in the consuming project *before* the package will compile.

Install these first:

| Dependency | Channel | How |
|---|---|---|
| `R3` | NuGet | [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) → install `R3` |
| `UniTask` | git URL | `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask` |

Which package needs what:

| Needs | Packages |
|---|---|
| `R3` | `input`, `ui` |
| `UniTask` | `audio`, `eqs` (optional asm), `input`, `save`, `ui` |

`unity-project-pckgs/Packages/manifest.json` is the reference — it has every one
of these wired up and is the configuration all packages are developed against.

## Linking into a Unity project (`link.sh`)

`link.sh` rewrites a consumer Unity project's `Packages/manifest.json`: a package
comes either from this checkout (`file:`, for editing it alongside the game) or
from the git URL pinned at a commit (what games commit).

```
./link.sh <project-path> [status|local|pin <sha>|remote] [package...]
```

- `status` — (default) each package's source: `LOCAL <path>`, `PINNED <sha>`, `REMOTE` (no sha) or `--`
- `local` — `file:<absolute path to this checkout>/packages/com.rubickanov.<name>` and the package goes into
  `testables` (absolute so copies and worktrees of a game work; `--relative` keeps the old relative path)
- `pin <sha>` — `https://github.com/rubickanov/unity-packages.git?path=packages/com.rubickanov.<name>#<sha>`, out of `testables`
- `remote` — the same URL without a sha (follows the default branch; prefer `pin`)
- `package...` — only these (`character` or `com.rubickanov.character`); a named package is added if missing.
  Without names, every package of this repo already in the manifest switches.

The loop for a fix in a package a game uses (e.g. `character`, shared by beach and leonid):

```
./link.sh ../beach local character        # 1. the game compiles this checkout
# 2. fix in packages/com.rubickanov.character, check it in the game (link the other game too if the fix is shared)
git commit && git push                    # 3. here
./link.sh ../beach pin <sha> character    # 4. in every game that uses it; commit its manifest + packages-lock.json
./link.sh ../leonid pin <sha> character
```

## Packages

| Package | Version | What |
|---|---|---|
| `audio` | 3.2.0 | Audio service: SFX pooling, music, mixer volume, 3D sound kinds, propagation delay, loudness, output true-peak limiter, mixer kit |
| `character` | 1.0.0 | Character body: KCC motor, Animancer animator, ragdoll and get-up, feet/head/seat IK (the game provides KCC and Animancer; not in `unity-project-pckgs`) |
| `devconsole` | 5.0.0 | In-game dev console |
| `eqs` | 2.0.0 | Environment Query System |
| `input` | 1.1.0 | Input System helpers each game needs |
| `log` | 1.2.0 | Named log channels |
| `save` | 1.0.0 | Player data store |
| `ui` | 4.1.0 | UI Toolkit framework |

## Running package tests from a consumer project

Package test assemblies in this repo are gated by a `UNITY_INCLUDE_TESTS`
define constraint and `includePlatforms: [Editor]` (see for example
`packages/com.rubickanov.ui/Tests/UI.Tests.asmdef`). Unity only compiles
those tests — and only shows them in **Window → General → Test Runner** — when
the package is both:

1. referenced as a local `file:` path in the consumer project's
   `Packages/manifest.json` (use `./link.sh <project> local`), and
2. listed in the `testables` array of that same `manifest.json`.

Add the packages you want to test to `testables` manually, alongside
`dependencies`:

```json
{
  "dependencies": {
    "com.rubickanov.ui": "file:../../unity-packages/packages/com.rubickanov.ui",
    "com.rubickanov.log": "file:../../unity-packages/packages/com.rubickanov.log"
  },
  "testables": [
    "com.rubickanov.ui",
    "com.rubickanov.log"
  ]
}
```

Then in Unity: **Window → General → Test Runner → EditMode** — the package's
tests will appear under its assembly name (e.g. `Rubickanov.UI.Tests`). Reimport the
package (right-click in Project → Reimport) if the tests don't show up after
editing the manifest.

Pin the project again with `./link.sh <project-path> pin <sha>` when
you're done — `testables` entries can stay in the manifest; they are ignored
for packages that aren't present.

## Generating docs (`docs/generate.sh`)

`docs/generate.sh` regenerates the DocFX site under `docs/_site/`. It scans
`packages/` for runtime `.asmdef` files (editor-only assemblies are skipped),
writes `docs/docfx.json`, copies each package's `README.md` into
`docs/guides/<slug>.md`, then runs `docfx metadata` and `docfx build`.

Prerequisite: the `docfx` CLI on your `PATH`.

```
./docs/generate.sh            # build into docs/_site/
./docs/generate.sh --serve    # build, then serve at http://localhost:8080
```

`docs/docfx.json`, `docs/index.md`, `docs/toc.yml`, `docs/guides/toc.yml`, and
`docs/api/index.md` are regenerated on every run — don't hand-edit them.
