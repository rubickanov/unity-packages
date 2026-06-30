# UI Loading

Bridge between the [UI](../com.rubickanov.ui/) framework and the [Loading](../com.rubickanov.loading/) pipeline. Adds `RegisterViewsOperation`, an `ILoadingOperation` that opens a `SceneViewScopeService` scope and registers a declared set of views while a scene loads.

## Dependencies

- `com.rubickanov.ui` — `SceneViewScopeService`, `ScopedViewRegistration`, `UILayer`, `IView`
- `com.rubickanov.loading` — `ILoadingOperation`, `ILoadingService`
- `UniTask` — async/await

## Quick Start

Build the operation, declaring each view and its layer, then hand it to the loading service on scene entry:

```csharp
var op = new RegisterViewsOperation(_scopeService)
    .Add<HudView>(UILayer.Screen)
    .Add<PausePopup>(UILayer.Popup)
    .Add<DamageNumberOverlay>(UILayer.Overlay);

await _loadingService.Load(new ILoadingOperation[] { op });
```

`ILoadingService.Load` takes an ordered list of operations, so `RegisterViewsOperation` sits alongside your asset-loading and world-spawn steps. On completion the declared views are registered with the UI service for the lifetime of the scene scope.

## Usage

### Scope ownership

The scope opened during `Execute` is owned by `SceneViewScopeService`, not by the operation. The operation never disposes it — neither on success nor on exception. Each call to `SceneViewScopeService.Begin()` auto-disposes the previous scope, so scope lifetime is tied to the scene, not to the loading run. Partial registrations from an interrupted run therefore stay valid until the next scene transition or service disposal.

### Single-use

Each `RegisterViewsOperation` is single-use. Calling `Execute` twice throws `InvalidOperationException`. Create a fresh instance per scene load.

### Duplicate registrations

Adding the same view type twice to one operation throws `InvalidOperationException` — duplicates are rejected rather than silently collapsed.

```csharp
new RegisterViewsOperation(_scopeService)
    .Add<HudView>(UILayer.Screen)
    .Add<HudView>(UILayer.Popup); // throws: HudView already added
```

### Custom description

`Description` is surfaced to loading presenters and defaults to `"Loading UI..."`. Override it for localized or contextual text:

```csharp
var op = new RegisterViewsOperation(_scopeService, description: "Preparing interface...")
    .Add<HudView>(UILayer.Screen);
```
