# UI Loading

Registers views as a step of a loading pipeline, into a fresh scene scope. Extension for [UI](../com.rubickanov.ui/), bridging it to [Loading](../com.rubickanov.loading/).

## Dependencies

> `UniTask` comes from a git URL, not from UPM — UPM will not pull it in for you. See [Third-party dependencies](https://github.com/rubickanov/unity-packages#third-party-dependencies).

- `com.rubickanov.ui` — base package: `SceneViewScopeService`, `ScopedViewRegistration`, `View`
- `com.rubickanov.loading` — `ILoadingOperation`, `ILoadingService`
- `UniTask` — async/await

## Quick Start

```csharp
var registerUi = new RegisterViewsOperation(sceneScopes)
    .Add<HudView>()
    .Add<PauseView>()
    .Add<ScoreboardView>();

await loadingService.Load(new ILoadingOperation[] { loadSector, registerUi, spawnShips });
```

Each view goes onto the layer it declares. `Execute` opens a new scope with `SceneViewScopeService.Begin()`, which disposes the previous scene's scope and unregisters its views, then registers the queued views in order and reports progress per view.

## Usage

### Scope ownership

The scope belongs to `SceneViewScopeService`, not to the operation: it is not disposed when the operation finishes or throws. It lives until the next `Begin()` or until the service is disposed. If the scope is replaced or disposed while a view is loading, `Execute` throws `OperationCanceledException` and no view of that scope stays registered.

### Rules

- An operation is single-use: a second `Execute` throws `InvalidOperationException`. Build one per scene load.
- Adding the same view type twice throws `InvalidOperationException`.
- A cancelled token before `Execute` starts leaves the current scope untouched.

### Presenter text

```csharp
var registerUi = new RegisterViewsOperation(sceneScopes, description: "Preparing bridge consoles...")
    .Add<HelmView>();
```

`Description` is shown by loading presenters; the default is `"Loading UI..."`.
