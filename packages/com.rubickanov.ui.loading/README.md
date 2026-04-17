# UI Loading

Bridge between the [UI](../com.rubickanov.ui/) framework and the [Loading](../com.rubickanov.loading/) pipeline. Exposes `RegisterViewsOperation` — an `ILoadingOperation` that opens a `SceneViewScopeService` scope and registers a declared set of views before the scene starts.

## Dependencies

- `com.rubickanov.ui` — `SceneViewScopeService`, `ScopedViewRegistration`, `UILayer`, `IView`
- `com.rubickanov.loading` — `ILoadingOperation`
- `UniTask` — async/await

## Quick Start

Add the operation to a loading pipeline on scene entry:

```csharp
var op = new RegisterViewsOperation(_scopeService)
    .Add<PausePopup>(UILayer.Popup)
    .Add<HudView>(UILayer.Screen)
    .Add<SettingsDialog>(UILayer.Dialog);

await _loadingPipeline.Run(op);
```

On completion the views are resolvable via the UI service for the lifetime of the scene scope.

## Usage

### Scope ownership

The scope opened by `Execute` is owned by `SceneViewScopeService`, **not** by the operation. The operation never disposes the scope — not on success and not on exception. Each call to `SceneViewScopeService.Begin()` auto-disposes the previous scope, so scope lifetime is tied to the scene, not to the loading run. Partial registrations from an interrupted `Execute` therefore remain valid until the next scene transition or service disposal.

### Single-use

Each `RegisterViewsOperation` is single-use. Calling `Execute` twice throws `InvalidOperationException`. Create a fresh instance per scene load.

### Duplicate registrations

Adding the same view type twice to one operation throws `InvalidOperationException` — the operation refuses silent duplicates.

### Custom description

The `Description` surfaced to presenters defaults to `"Loading UI..."`. Override it for localized or contextual text:

```csharp
var op = new RegisterViewsOperation(_scopeService, description: loc.GetString(LocKeys.Loading_UI))
    .Add<HudView>(UILayer.Screen);
```
