# State Machine

Generic finite state machine with hierarchical support, deferred transitions, and zero-allocation runtime.

## Features

- Generic `StateMachine<TKey>` with dictionary-based state lookup
- `SubStateMachine<TKey>` for hierarchical state machines
- `CallbackState` + fluent extensions for lambda-based states
- Re-entrancy safe with deferred transitions
- Zero dependencies, zero-alloc runtime

## Usage

```csharp
var fsm = new StateMachine<GameState>()
    .AddState(GameState.Menu, new MenuState())
    .AddState(GameState.Playing, new PlayingState())
    .AddState(GameState.Paused, new PausedState());

fsm.Enter(GameState.Menu);
fsm.Transition(GameState.Playing);
```
