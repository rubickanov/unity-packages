# Loading

Generic loading pipeline with sequential operations, progress tracking, and loading presenter abstraction.

## Overview

`LoadingService` executes a sequence of `ILoadingOperation`s, reporting progress via `ILoadingPresenter`. Operations run sequentially in the order provided — the caller controls execution order by position in the array.

Scene loading is not baked into the pipeline. Use the included `LoadSceneOperation` to load a Unity scene as a regular operation alongside other operations.

## Key Types

| Type | Description |
|------|-------------|
| `ILoadingService` | Interface for the generic loading pipeline |
| `ILoadingOperation` | Single async operation with description and progress |
| `ILoadingPresenter` | UI presenter abstraction for loading progress |
| `NullLoadingPresenter` | No-op presenter for headless/server builds |
| `LoadingService` | Default implementation that orchestrates sequential operations |
| `LoadSceneOperation` | Loads and activates a Unity scene as a loading operation |

## Usage

```csharp
// Register in DI container
builder.Register<LoadingService>(Lifetime.Singleton).As<ILoadingService>();

// Execute a loading pipeline
await loadingService.Load(new ILoadingOperation[]
{
    new RegisterViewsOperation(ui),
    new LoadSceneOperation("Gameplay"),
    new ConnectToServerOperation(session),
    new WaitForSpawnOperation(session),
});
```

### Custom Loading Operation

```csharp
public class MyOperation : ILoadingOperation
{
    public string Description => "Doing something...";

    public async UniTask Execute(IProgress<float> progress, CancellationToken ct)
    {
        progress.Report(0f);
        // ... async work ...
        progress.Report(1f);
    }
}
```
