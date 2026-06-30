# State Machine

Generic finite state machine with hierarchical support, deferred transitions, and a sync/async pair of runtimes. The core runtime is pure C# with no engine references and allocates only once at setup (a backing dictionary); updates and transitions are allocation-free.

## Dependencies

- Sync runtime (`Rubickanov.StateMachine.Runtime`) — none.
- Async runtime (`Rubickanov.StateMachine.Async`) — [UniTask](https://github.com/Cysharp/UniTask), for awaitable enter/exit.

Unity `6000.0`+.

## Architecture

```
IState { OnEnter, OnUpdate, OnExit }
├── StateBase        — abstract, virtual no-op defaults
├── CallbackState    — lambda-backed (onEnter, onUpdate, onExit)
└── SubStateMachine<TKey> : StateMachine<TKey>, IState

StateMachine<TKey>   — dictionary lookup, deferred transitions, StateChanged event
```

The async runtime mirrors this shape: `IAsyncState`, `AsyncStateBase`, `AsyncCallbackState`, `AsyncSubStateMachine<TKey>`, and `AsyncStateMachine<TKey>`. It is a separate, parallel implementation — `AsyncStateMachine` does not derive from `StateMachine`.

A `SubStateMachine` is itself a full state machine that also implements the state interface, which is what enables nesting: register it as a state inside a parent machine and it starts/stops with the parent.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.StateMachine.Runtime** | No | Sync FSM, states, hierarchy. Pure C#. |
| **Rubickanov.StateMachine.Async** | Yes | Async FSM with awaitable enter/exit. Depends on UniTask. |

## Core Concepts

**IState / IAsyncState** — three lifecycle methods: `OnEnter()`, `OnUpdate(float)`, `OnExit()`. In the async variant `Enter`/`Exit` are awaitable and receive a `CancellationToken`; `OnUpdate` stays synchronous because it runs every frame.

**StateMachine\<TKey\> / AsyncStateMachine\<TKey\>** — generic FSM keyed by any non-null type (enum, string, int). Dictionary-based lookup. Transitions requested during `OnEnter`/`OnExit` are deferred to keep re-entrancy safe (max chained depth 16).

**SubStateMachine\<TKey\> / AsyncSubStateMachine\<TKey\>** — machines that also implement the state interface, enabling hierarchical state machines. They start at a configured initial state on parent enter and stop on parent exit.

## Quick Start

```csharp
enum GamePhase { Menu, Playing, Paused }

var fsm = new StateMachine<GamePhase>();
fsm.AddState(GamePhase.Menu, new MenuState());
fsm.AddState(GamePhase.Playing, new PlayingState());
fsm.AddState(GamePhase.Paused, new PausedState());

fsm.Start(GamePhase.Menu);
```

States must be registered before `Start()`; calling `AddState` after the machine is started throws.

## Usage

### Defining States

Subclass **StateBase** for states with class-level logic. Override only what you need:

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

Transitions are deferred when `SetState()` is called during `OnEnter()` or `OnExit()`, preventing re-entrancy issues; the pending transition runs after the current one completes. A chained depth cap of 16 guards against infinite transition loops.

Calling `SetState(CurrentKey)` — a self-transition — is a no-op; the state is not re-entered.

If multiple `SetState` calls happen during a single `OnEnter`/`OnExit`, only the last one executes — earlier queued keys are overwritten (last-write-wins).

`SetState` on a key that was never registered throws `ArgumentException`.

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
GamePhase current = fsm.CurrentKey;       // default before Start / after Stop
IState? state     = fsm.CurrentState;     // null before Start / after Stop
bool isPlaying    = fsm.IsInState(GamePhase.Playing);
bool isRunning    = fsm.IsStarted;

bool hasQueued    = fsm.HasPendingTransition;
GamePhase next    = fsm.PendingKey;        // default if nothing pending
```

### State Change Events

```csharp
fsm.StateChanged += (previous, next) =>
{
    Debug.Log($"Transition: {previous} -> {next}");
};
```

`StateChanged` fires after the new state's `OnEnter` completes. For chained deferred transitions it fires once per hop (A→B→C fires `(A, B)` then `(B, C)`). The initial `Start()` does not fire `StateChanged`.

### Lambda States

Use the fluent extension for quick inline states. It wraps a `CallbackState` and returns the machine for chaining:

```csharp
new StateMachine<GamePhase>()
    .AddState(GamePhase.Menu,
        onEnter: () => ShowMainMenu(),
        onExit:  () => HideMainMenu())
    .AddState(GamePhase.Playing,
        onEnter:  () => ResumeTime(),
        onUpdate: dt => TickGameplay(dt),
        onExit:   () => PauseTime());
```

### Retrieving States

```csharp
var playing = fsm.GetState<PlayingState>(GamePhase.Playing);
var current = fsm.GetCurrentState<PlayingState>();
```

Both return `null` if the key is missing or the stored state is not the requested type.

### Stopping

```csharp
fsm.Stop();  // calls OnExit on the current state, resets the FSM
```

After `Stop()`, `CurrentKey` returns `default(TKey)` and `CurrentState` returns `null`. `Stop()` on a machine that was never started is a no-op.

### Custom Key Comparer

```csharp
var fsm = new StateMachine<string>(StringComparer.OrdinalIgnoreCase);
```

The comparer is used for both dictionary lookups and `IsInState`.

### Hierarchical State Machines

**SubStateMachine\<TKey\>** nests a full FSM inside a parent state. Register it like any other state; it starts at its configured initial state on enter and stops on exit:

```csharp
enum CombatPhase { Aiming, Firing, Reloading }

var combat = new SubStateMachine<CombatPhase>(CombatPhase.Aiming);
combat.AddState(CombatPhase.Aiming, new AimingState());
combat.AddState(CombatPhase.Firing, new FiringState());
combat.AddState(CombatPhase.Reloading, new ReloadingState());

var fsm = new StateMachine<GamePhase>();
fsm.AddState(GamePhase.Playing, combat);
fsm.AddState(GamePhase.Paused, new PausedState());
```

When the parent enters `GamePhase.Playing`, the sub-machine starts at `CombatPhase.Aiming`; when the parent leaves `Playing`, the sub-machine stops. The parent's `Update` flows through to the active sub-state.

## Async State Machines

For states with long-running enter/exit — asset loading, scene warmup, network handshake, teardown with awaitable cleanup — use `AsyncStateMachine<TKey>` and `IAsyncState`.

### Differences from the Sync Runtime

- `OnEnterAsync(CancellationToken)` and `OnExitAsync(CancellationToken)` return `UniTask` and can await.
- `OnUpdate(float)` stays synchronous — it runs per frame.
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
- For a deferred transition (a `SetStateAsync` issued during another transition), the token from the deferred call is preserved and used when the queued transition runs — not the token from the outer transition.
- The FSM does not create per-state tokens. If a state spawns fire-and-forget background work, manage its lifecycle explicitly in `OnExitAsync`.
- Cancellation is recoverable: an `OperationCanceledException` thrown out of an enter/exit unwinds the transition and resets the machine's internal flags, so the FSM stays usable afterward.

## Design Decisions

- **Deferred transitions** — calling `SetState()` inside `OnEnter()`/`OnExit()` queues the transition instead of executing immediately, preventing stack overflow and ensuring each state completes its lifecycle. The depth-16 cap catches accidental infinite loops.
- **Pure-C# core** — the sync runtime sets `noEngineReferences: true`, so it runs in server builds, tests, and non-Unity contexts. The async runtime is a separate assembly that adds UniTask as its only dependency.
- **Generic TKey** — enum, string, or any type with equality. Value-type keys with the default comparer transition without boxing.
- **Sync and async are separate runtimes** — `AsyncStateMachine` is not derived from `StateMachine`; each is optimized for its execution model rather than sharing a lowest-common-denominator base.
- **Not thread-safe** — designed for single-threaded access (the game main loop). Do not call concurrently from multiple threads.
