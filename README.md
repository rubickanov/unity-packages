# Unity Packages

Shared Unity packages.

## Packages

| Package | Description |
|---------|-------------|
| `com.rubickanov.acs` | Aspect-Component System |
| `com.rubickanov.acs.network` | ACS Netcode extension |
| `com.rubickanov.audio` | Audio service |
| `com.rubickanov.behaviortree` | Behavior tree editor & runtime |
| `com.rubickanov.devconsole` | In-game developer console |
| `com.rubickanov.loading` | Loading pipeline |
| `com.rubickanov.localization` | Localization service |
| `com.rubickanov.logging` | ZLogger integration |
| `com.rubickanov.storage` | Key-value storage |
| `com.rubickanov.ui` | UI framework |
| `com.rubickanov.ui.animations` | LitMotion view animations |

## Installation

Unity Package Manager → Add package from git URL:

```
git+ssh://git@github.com/rubickanov-org/unity-packages.git?path=packages/com.rubickanov.<NAME>
```

Or HTTPS:

```
https://github.com/rubickanov-org/unity-packages.git?path=packages/com.rubickanov.<NAME>
```

Replace `<NAME>` with the package from the table above (e.g. `ui`).

Pin to a specific commit by appending `#<hash>`:

```
git+ssh://git@github.com/rubickanov-org/unity-packages.git?path=packages/com.rubickanov.ui#a1b2c3d
```
