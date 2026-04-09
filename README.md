# Unity Packages

Shared Unity packages.

## Installation

Unity Package Manager → Add package from git URL:

```
git+ssh://git@github.com/rubickanov-org/unity-packages.git?path=packages/com.rubickanov.<NAME>
```

Or HTTPS:

```
https://github.com/rubickanov-org/unity-packages.git?path=packages/com.rubickanov.<NAME>
```

Replace `<NAME>` with a package folder under `packages/` (e.g. `ui`).

Pin to a specific commit by appending `#<hash>`:

```
git+ssh://git@github.com/rubickanov-org/unity-packages.git?path=packages/com.rubickanov.ui#a1b2c3d
```

## Linking into a Unity project (`link.sh`)

`link.sh` rewrites a consumer Unity project's `Packages/manifest.json` to point
at either local `file:` paths (for editing packages alongside the game) or the
git remote URLs (for release builds). Only packages already listed in the
target manifest are touched.

```
./link.sh <project-path> [local|remote|status]
```

- `local` — rewrite each matching entry to `file:<relative-path>/com.rubickanov.<name>`
- `remote` — rewrite each matching entry to the `git+ssh://…?path=packages/com.rubickanov.<name>` form
- `status` — (default) list which packages are currently LOCAL, REMOTE, or not in the manifest

Example:

```
./link.sh <project-path> local      # switch to local editing
./link.sh <project-path> remote     # switch back for release
./link.sh <project-path>            # inspect current state
```

## Running package tests from a consumer project

Package test assemblies in this repo are gated by a `UNITY_INCLUDE_TESTS`
define constraint and `includePlatforms: [Editor]` (see for example
`packages/com.rubickanov.acs/Tests/ACS.Tests.asmdef`). Unity only compiles
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
    "com.rubickanov.acs": "file:../../unity-packages/packages/com.rubickanov.acs",
    "com.rubickanov.utils": "file:../../unity-packages/packages/com.rubickanov.utils"
  },
  "testables": [
    "com.rubickanov.acs",
    "com.rubickanov.utils"
  ]
}
```

Then in Unity: **Window → General → Test Runner → EditMode** — the package's
tests will appear under its assembly name (e.g. `ACS.Tests`). Reimport the
package (right-click in Project → Reimport) if the tests don't show up after
editing the manifest.

Switch the project back to `remote` with `./link.sh <project-path> remote` when
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
