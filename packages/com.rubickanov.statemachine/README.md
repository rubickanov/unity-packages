# State Machine

Generic finite state machine with hierarchical support, deferred transitions, and zero-allocation runtime. Pure C#, no engine references.

## Dependencies

None.

## Architecture

```
IState { OnEnter, OnUpdate, OnExit }
├── StateBase           — abstract, virtual no-op defaults
├── CallbackState       — lambda-based (onEnter, onUpdate, onExit)
└── SubStateMachine<TKey> — nested FSM that implements IState
        │
        └── StateMachine<TKey> — dictionary lookup, deferred transitions
```

## Core Concepts

**IState** — Interface with three lifecycle methods: `OnEnter()`, `OnUpdate(float deltaTime)`, `OnExit()`.

**StateMachine\<TKey\>** — Generic FSM keyed by any type (enum, string, int). Dictionary-based state lookup. Deferred transitions when `SetState()` is called during `OnEnter`/`OnExit` (re-entrancy safe, max depth 16).

**SubStateMachine\<TKey\>** — A **StateMachine\<TKey\>** that also implements **IState**, enabling hierarchical state machines. Automatically starts/stops the sub-FSM on enter/exit.

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

Transitions are deferred if called during `OnEnter()` or `OnExit()` to prevent re-entrancy issues. The pending transition executes after the current one completes.

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
GamePhase current = fsm.CurrentKey;
bool isPlaying = fsm.IsInState(GamePhase.Playing);
bool isRunning = fsm.IsStarted;
```

### State Change Events

```csharp
fsm.StateChanged += (previous, next) =>
{
    Debug.Log($"Transition: {previous} -> {next}");
};
```

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

### Hierarchical State Machines

**SubStateMachine\<TKey\>** nests a full FSM inside a parent state. It starts on enter and stops on exit:

```csharp
enum CombatPhase { Aiming, Firing, Reloading }

var combatSub = new SubStateMachine<CombatPhase>(CombatPhase.Aiming);
combatSub.AddState(CombatPhase.Aiming, new AimingState());
combatSub.AddState(CombatPhase.Firing, new FiringState());
combatSub.AddState(CombatPhase.Reloading, new ReloadingState());

// Use as a regular state in the parent FSM
var fsm = new StateMachine<GamePhase>();
fsm.AddState(GamePhase.Playing, combatSub);
fsm.AddState(GamePhase.Paused, new PausedState());
```

When the parent enters `GamePhase.Playing`, the sub-machine starts at `CombatPhase.Aiming`. When the parent exits, the sub-machine stops.

### Retrieving States

```csharp
var playing = fsm.GetState<PlayingState>(GamePhase.Playing);
```

Returns `null` if the key is not registered or the type does not match.

## Design Decisions

- **Deferred transitions** — calling `SetState()` inside `OnEnter()`/`OnExit()` queues the transition instead of executing immediately. Prevents stack overflow and ensures each state completes its lifecycle. Max depth of 16 catches infinite loops.
- **No engine references** — pure C# with `noEngineReferences: true`. Usable in server builds, tests, or non-Unity contexts.
- **Generic TKey** — enum, string, or any type that implements equality. No boxing when using value types with the default comparer.
