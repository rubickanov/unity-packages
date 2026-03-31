# Loading

Generic loading pipeline that executes sequential async operations with progress tracking and a presenter abstraction.

## Dependencies

- `UniTask` — async operation execution
- `ZLogger` — structured logging

## Architecture

```
ILoadingService ──► ILoadingOperation[]
       │                    │
       ▼                    ▼
 LoadingService    LoadSceneOperation
       │           (or any custom op)
       ▼
ILoadingPresenter
├── (game code implements for UI)
└── NullLoadingPresenter — no-op for server builds
```

**LoadingService** executes operations sequentially in the order provided. Each operation receives a progress reporter scoped to its slice of the total progress bar. The **ILoadingPresenter** receives description text, progress updates, and error messages -- game code bridges it to a UI implementation.

## Quick Start

1. Implement `ILoadingPresenter` in game code (or use **NullLoadingPresenter** for headless builds).
2. Register in your LifetimeScope:

```csharp
builder.Register<LoadingService>(Lifetime.Singleton).As<ILoadingService>();
builder.Register<NullLoadingPresenter>(Lifetime.Singleton).As<ILoadingPresenter>();
```

3. Build a pipeline and load:

```csharp
await loadingService.Load(new ILoadingOperation[]
{
    new LoadSceneOperation("Gameplay"),
    new ConnectToServerOperation(session),
});
```

## Usage

### Running a Loading Pipeline

```csharp
await loadingService.Load(new ILoadingOperation[]
{
    new RegisterViewsOperation(uiService),
    new LoadSceneOperation("Arena"),
    new ConnectToServerOperation(networkSession),
    new WaitForSpawnOperation(networkSession),
});
```

### Custom Loading Operation

```csharp
public class ConnectToServerOperation : ILoadingOperation
{
    public string Description => "Connecting to server...";

    private readonly NetworkSession _session;

    public ConnectToServerOperation(NetworkSession session) => _session = session;

    public async UniTask Execute(IProgress<float> progress, CancellationToken ct)
    {
        progress.Report(0f);
        await _session.ConnectAsync(ct);
        progress.Report(1f);
    }
}
```

### Loading a Scene

**LoadSceneOperation** loads and activates a Unity scene as a standard pipeline operation.

```csharp
new LoadSceneOperation("Gameplay")
```

### Implementing a Presenter

```csharp
public class UILoadingPresenter : ILoadingPresenter
{
    private readonly IUIService _ui;

    public UILoadingPresenter(IUIService ui) => _ui = ui;

    public async UniTask Show() => await _ui.ShowScreenAsync<LoadingScreen>();
    public void SetProgress(float progress) => _ui.Get<LoadingScreen>().SetProgress(progress);
    public void SetDescription(string description) => _ui.Get<LoadingScreen>().SetDescription(description);
    public void SetError(string error) => _ui.Get<LoadingScreen>().SetError(error);
    public void Hide() => _ui.Hide<LoadingScreen>();
}
```

### Handling Errors

`Load()` returns a `LoadResult` — the caller decides how to handle failures:

```csharp
var result = await loadingService.Load(operations);
if (!result.Success)
{
    ShowErrorPopup($"Loading failed: {result.Error?.Message}");
}
```

## Design Decisions

- **ILoadingPresenter instead of IUIService** — decouples the loading pipeline from any UI framework. Game code provides the bridge.
- **Uniform progress distribution** — each operation gets an equal slice of the progress bar (1/N). Individual operations report 0-1 within their slice.
- **Caller controls order** — operations execute in array order. No priority system or `RunBefore`/`RunAfter` attributes.
- **LoadResult instead of fallback/callbacks** — the caller owns error recovery, not the loading service. This avoids ownership issues where a fallback scene load destroys the caller.
