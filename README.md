# Unity Packages

Shared Unity packages.

## Packages

| Package | Description |
|---------|-------------|
| `com.rubickanov.acs` | Aspect-Component System |
| `com.rubickanov.audio` | Audio service |
| `com.rubickanov.behaviortree` | Behavior tree editor & runtime |
| `com.rubickanov.camera` | Cinemachine camera service |
| `com.rubickanov.devconsole` | In-game developer console |
| `com.rubickanov.loading` | Loading pipeline |
| `com.rubickanov.localization` | Localization service |
| `com.rubickanov.logging` | ZLogger integration |
| `com.rubickanov.storage` | Key-value storage |
| `com.rubickanov.ui` | UI framework |
| `com.rubickanov.ui.animations` | LitMotion view animations |

## Installation

Add to `Packages/manifest.json`:

```json
"com.rubickanov.ui": "git+ssh://git@github.com/rubickanov-org/unity-packages.git?path=packages/com.rubickanov.ui"
```

Pin to a specific commit:

```json
"com.rubickanov.ui": "git+ssh://git@github.com/rubickanov-org/unity-packages.git?path=packages/com.rubickanov.ui#a1b2c3d"
```

Requires SSH key with access to this repo.
