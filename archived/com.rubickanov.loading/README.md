# Loading

Generic loading pipeline that runs a sequence of async operations with progress tracking, optional two-phase (load then activate) execution, and a UI-agnostic presenter abstraction.

## Dependencies

> `UniTask` and `ZLogger` come from git URLs, not from UPM — UPM will not pull them in for you. See [Third-party dependencies](https://github.com/rubickanov/unity-packages#third-party-dependencies).

- `UniTask` — async operation execution (`UniTask` / `UniTask<T>` return types)
- `ZLogger` + `Microsoft.Extensions.Logging` — `LoadingService` requires an `ILoggerFactory`; pipeline steps and failures are logged through it

`LoadingService` takes the logger factory as a constructor argument, so the consuming project must provide a `Microsoft.Extensions.Logging` implementation (e.g. from `com.rubickanov.logging`).

Unity 6000.0+.

## Architecture

```
ILoadingService ──► ILoadingOperation[]      (run in list order)
       │                    │
       ▼                    ▼
 LoadingService    ┌── Execute(progress, ct)       phase 1: load
       │           └── Activate(ct)  (deferrable)  phase 2: activate
       ▼
ILoadingPresenter
├── game code implements for UI
└── NullLoadingPresenter — no-op for server / headless builds
```

`LoadingService` executes operations sequentially. Each operation gets an equal slice of the progress bar (`1/N`) and reports `0–1` within its own slice. The **ILoadingPresenter** receives description text, progress, and error messages; game code bridges it to an actual UI.

## Core Concepts

**ILoadingOperation** — A single async step. Exposes a `Description` and an `Execute(IProgress<float>, CancellationToken)` method.

**IDeferrableOperation** — An operation that splits into two phases: `Execute` loads the work, and `Activate` commits it later. `LoadingService` runs every operation's `Execute` first, then (optionally after a user-input gate) calls `Activate` on the deferrable ones, in list order. This lets all operations finish loading before anything visibly switches — e.g. load a scene to 90% during the bar, then activate it only once the player presses a key.

**ILoadingPresenter** — The loading UI surface. Decoupled from any UI framework so the pipeline carries no UI dependency.

## Quick Start

1. Implement `ILoadingPresenter` in game code (or use **NullLoadingPresenter** for headless builds).
2. Register in your LifetimeScope:

```csharp
builder.Register<LoadingService>(Lifetime.Singleton).As<ILoadingService>();
builder.Register<UILoadingPresenter>(Lifetime.Singleton).As<ILoadingPresenter>();
```

3. Build a pipeline and run it:

```csharp
var result = await loadingService.Load(new ILoadingOperation[]
{
    new LoadSceneOperation("Gameplay"),
    new ConnectToServerOperation(session),
});
```

`Load` takes an `IReadOnlyList<ILoadingOperation>`, so an array works directly.

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

`progress.Report` is normalized to `0–1`; the service maps it into this operation's slice of the overall bar.

### Two-Phase (Deferrable) Operation

Implement `IDeferrableOperation` when work must finish loading during the bar but only take visible effect later. **LoadSceneOperation** does exactly this: `Execute` loads the scene to 90% with activation disabled, and `Activate` flips `allowSceneActivation`.

```csharp
public class SpawnArenaOperation : ILoadingOperation, IDeferrableOperation
{
    public string Description => "Preparing arena...";

    private Arena _arena;

    public async UniTask Execute(IProgress<float> progress, CancellationToken ct)
    {
        _arena = await Arena.PreloadAsync(progress, ct); // heavy work, reports 0..1
    }

    public UniTask Activate(CancellationToken ct)
    {
        _arena.Show(); // cheap commit, runs after all Execute calls
        return UniTask.CompletedTask;
    }
}
```

Activations run after every operation's `Execute` completes. A failed `Activate` aborts the pipeline but does NOT roll back already-activated operations — keep activations safe against partial state.

### Waiting for User Input

Pass `waitForInput: true` to gate activation behind `ILoadingPresenter.WaitForInput`. All operations load, the presenter shows a "Press any key" prompt, and deferred activations run only after input.

```csharp
await loadingService.Load(operations, waitForInput: true, ct: cancellationToken);
```

### Loading a Scene

**LoadSceneOperation** loads and activates a Unity scene. Activation is deferred (it implements `IDeferrableOperation`).

```csharp
new LoadSceneOperation("Gameplay")
new LoadSceneOperation("Hub", LoadSceneMode.Additive)
new LoadSceneOperation("Arena", description: "Preparing the arena...")
```

The scene must be listed in Build Settings, otherwise `Execute` throws.

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
    public UniTask WaitForInput(CancellationToken ct) => _ui.Get<LoadingScreen>().WaitForAnyKey(ct);
    public async UniTask Hide() => await _ui.HideAsync<LoadingScreen>();
}
```

`Show` is awaited in parallel with the first operations, so the presenter must accept `SetProgress` / `SetDescription` calls before `Show` completes. `Hide` may be called without a preceding `Show` (the service issues a defensive `Hide` at the start of each `Load`), so it must be idempotent.

### Handling Errors and Cancellation

`Load` returns a `LoadResult` with a `Status` of `Ok`, `Cancelled`, or `Failed`.

```csharp
var result = await loadingService.Load(operations, ct: cancellationToken);
switch (result.Status)
{
    case LoadStatus.Ok:        break;
    case LoadStatus.Cancelled: break; // caller's token was cancelled
    case LoadStatus.Failed:    ShowErrorPopup($"Loading failed: {result.Error?.Message}"); break;
}
```

`LoadResult` also exposes `Success` (`Status == Ok`) and `Cancelled` (`Status == Cancelled`) shortcuts. On `Failed`, the presenter's `SetError` is called before `Hide`.

### Customizing the Default Description

The service shows `defaultDescription` after `Show` and before the first operation sets its own.

```csharp
new LoadingService(presenter, loggerFactory, defaultDescription: "Загрузка...");
```

## Design Decisions

- **ILoadingPresenter instead of IUIService** — decouples the pipeline from any UI framework. Game code provides the bridge.
- **Uniform progress distribution** — each operation gets an equal `1/N` slice of the bar; operations report `0–1` within their slice.
- **Two-phase activation** — `Execute` does the heavy loading, `Activate` commits it. All loads finish before anything switches, enabling clean "press any key" gates via `waitForInput`.
- **Caller controls order** — operations execute in list order. No priority system or `RunBefore`/`RunAfter` attributes.
- **LoadResult instead of fallback/callbacks** — the caller owns error recovery, not the service. Avoids ownership issues where a fallback scene load destroys the caller.
- **Distinct `Cancelled` state** — external cancellation is semantically different from success; the caller decides whether to navigate back, retry, or leave the UI alone.
- **Reentry cancel is not a failure** — starting a new `Load` while one is in flight cancels the earlier one, which resolves as `Ok`. Only external `CancellationToken` cancellation resolves as `Cancelled`.
- **Late progress reports are dropped** — once an operation completes, stale `IProgress<float>.Report` calls are ignored (epoch-based guard) so they cannot overwrite the next operation's progress.
- **Not thread-safe** — internal state (`CancellationTokenSource`, generation counter) is unsynchronized; call `Load` from a single thread, typically Unity's main thread.
