# State Machine

Generic finite state machine with hierarchical support, deferred transitions, and a sync/async pair of runtimes. Pure C#, no engine references. Zero allocations per update and transition; setup allocates a backing dictionary once.

## Dependencies

- Sync runtime: none.
- Async runtime: [UniTask](https://github.com/Cysharp/UniTask).

## Architecture

```
IState { OnEnter, OnUpdate, OnExit }
├── StateBase           — abstract, virtual no-op defaults
├── CallbackState       — lambda-based (onEnter, onUpdate, onExit)
└── SubStateMachine<TKey> — nested FSM that implements IState
        │
        └── StateMachine<TKey> — dictionary lookup, deferred transitions

IAsyncState { OnEnterAsync(ct), OnUpdate, OnExitAsync(ct) }
├── AsyncStateBase           — abstract, virtual no-op defaults
├── AsyncCallbackState       — lambda-based
└── AsyncSubStateMachine<TKey> — nested async FSM
        │
        └── AsyncStateMachine<TKey> — UniTask-based transitions, CancellationToken propagation
```

## Core Concepts

**IState / IAsyncState** — three lifecycle methods: `OnEnter()`, `OnUpdate(float)`, `OnExit()`. In the async variant, `Enter`/`Exit` are awaitable and receive a `CancellationToken`; `OnUpdate` remains synchronous because it runs per frame.

**StateMachine\<TKey\> / AsyncStateMachine\<TKey\>** — generic FSM keyed by any type (enum, string, int). Dictionary-based lookup. Deferred transitions when `SetState()` is called during `OnEnter`/`OnExit` (re-entrancy safe, max depth 16).

**SubStateMachine\<TKey\> / AsyncSubStateMachine\<TKey\>** — FSMs that also implement the state interface, enabling hierarchical state machines. Automatically starts/stops the sub-FSM on parent enter/exit.

## Quick Start

```csharp
enum GamePhase { Menu, Playing, Paused }

var fsm = new StateMachine<GamePhase>();
fsm.AddState(GamePhase.Menu, new MenuState());
fsm.AddState(GamePhase.Playing, new PlayingState());
fsm.AddState(GamePhase.Paused, new PausedState());

fsm.Start(GamePhase.Menu);
```

## Usage

### Defining States

Subclass **StateBase** for states with class-level logic:

```csharp
public class PlayingState : StateBase
{
    public override void OnEnter()
    {
        // resume game time
    }

    public override void OnUpdate(float deltaTime)
    {
        // tick gameplay systems
    }

    public override void OnExit()
    {
        // cleanup
    }
}
```

### Transitions

```csharp
fsm.SetState(GamePhase.Playing);
```

Transitions are deferred if called during `OnEnter()` or `OnExit()` to prevent re-entrancy issues. The pending transition executes after the current one completes. Max chained depth of 16 guards against infinite transition loops.

Calling `SetState(CurrentKey)` — a self-transition — is a no-op; the state is not re-entered.

If multiple `SetState` calls happen during `OnEnter`/`OnExit`, only the last one executes — earlier queued keys are overwritten (last-write-wins).

### Updating

Call `Update()` each frame to tick the current state:

```csharp
void Update()
{
    fsm.Update(Time.deltaTime);
}
```

### Querying State

```csharp
GamePhase current = fsm.CurrentKey;      // returns default before Start / after Stop
IState? state   = fsm.CurrentState;      // null before Start / after Stop
bool isPlaying  = fsm.IsInState(GamePhase.Playing);
bool isRunning  = fsm.IsStarted;

bool hasQueued  = fsm.HasPendingTransition;
GamePhase next  = fsm.PendingKey;        // default if nothing pending
```

### State Change Events

```csharp
fsm.StateChanged += (previous, next) =>
{
    Debug.Log($"Transition: {previous} -> {next}");
};
```

The event fires after the new state's `OnEnter` completes. For chained deferred transitions, it fires once per hop (A→B→C fires `StateChanged(A, B)` then `StateChanged(B, C)`). The initial `Start()` does not fire `StateChanged`.

### Lambda States

Use `CallbackState` directly or the fluent extension for quick inline states:

```csharp
var fsm = new StateMachine<GamePhase>();

fsm.AddState(GamePhase.Menu,
    onEnter: () => ShowMainMenu(),
    onExit: () => HideMainMenu());

fsm.AddState(GamePhase.Playing,
    onEnter: () => ResumeTime(),
    onUpdate: dt => TickGameplay(dt),
    onExit: () => PauseTime());
```

The extension method returns the state machine for chaining `AddState` calls.

### Stopping

```csharp
fsm.Stop();  // calls OnExit on current state, resets the FSM
```

After `Stop()`, `CurrentKey` returns `default(TKey)` and `CurrentState` returns `null`.

### Hierarchical State Machines

**SubStateMachine\<TKey\>** nests a full FSM inside a parent state. It starts at its configured initial state on enter and stops on exit:

```csharp
enum CombatPhase { Aiming, Firing, Reloading }

var combatSub = new SubStateMachine<CombatPhase>(CombatPhase.Aiming);
combatSub.AddState(CombatPhase.Aiming, new AimingState());
combatSub.AddState(CombatPhase.Firing, new FiringState());
combatSub.AddState(CombatPhase.Reloading, new ReloadingState());

var fsm = new StateMachine<GamePhase>();
fsm.AddState(GamePhase.Playing, combatSub);
fsm.AddState(GamePhase.Paused, new PausedState());
```

When the parent enters `GamePhase.Playing`, the sub-machine starts at `CombatPhase.Aiming`. When the parent exits, the sub-machine stops.

### Retrieving States

```csharp
var playing = fsm.GetState<PlayingState>(GamePhase.Playing);
var current = fsm.GetCurrentState<PlayingState>();
```

Both return `null` if the key is missing or the type does not match.

### Custom Key Comparer

```csharp
var fsm = new StateMachine<string>(StringComparer.OrdinalIgnoreCase);
```

The comparer is used for both dictionary lookups and `IsInState`.

## Async State Machines

For states with long-running `Enter`/`Exit` — asset loading, scene warmup, network handshake, teardown with awaitable cleanup — use `AsyncStateMachine<TKey>` and `IAsyncState`.

### Differences from the Sync Runtime

- `OnEnterAsync(CancellationToken)` and `OnExitAsync(CancellationToken)` return `UniTask` and can await.
- `OnUpdate(float)` remains synchronous — it runs per frame.
- `StartAsync`, `SetStateAsync`, `StopAsync` all return `UniTask` and accept an optional `CancellationToken`.

### Example

```csharp
public class LoadingState : AsyncStateBase
{
    public override async UniTask OnEnterAsync(CancellationToken ct)
    {
        await LoadAssets(ct);
    }

    public override async UniTask OnExitAsync(CancellationToken ct)
    {
        await UnloadAssets(ct);
    }
}

var fsm = new AsyncStateMachine<GamePhase>();
fsm.AddState(GamePhase.Loading, new LoadingState());

using var cts = new CancellationTokenSource();
await fsm.StartAsync(GamePhase.Loading, cts.Token);
```

Lambda form via the extension:

```csharp
fsm.AddState(GamePhase.Loading,
    onEnterAsync: ct => LoadAssets(ct),
    onExitAsync:  ct => UnloadAssets(ct));
```

### CancellationToken Semantics

- The token passed to `StartAsync`/`SetStateAsync`/`StopAsync` is forwarded to the state's `OnEnterAsync`/`OnExitAsync`.
- For deferred transitions (a `SetStateAsync` call issued during another transition), the token from the deferred call is preserved and used when the queued transition executes — not the token from the outer transition.
- The FSM does **not** create per-state tokens. If your state spawns fire-and-forget background work, manage its lifecycle explicitly in `OnExitAsync`.

## Design Decisions

- **Deferred transitions** — calling `SetState()` inside `OnEnter()`/`OnExit()` queues the transition instead of executing immediately. Prevents stack overflow and ensures each state completes its lifecycle. Max depth of 16 catches infinite loops.
- **No engine references** — pure C# with `noEngineReferences: true`. Usable in server builds, tests, or non-Unity contexts. The async assembly adds UniTask as the only dependency.
- **Generic TKey** — enum, string, or any type that implements equality. No boxing when using value types with the default comparer.
- **Not thread-safe** — designed for single-threaded access (game main loop). Do not call concurrently from multiple threads.
