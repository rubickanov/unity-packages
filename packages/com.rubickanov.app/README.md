# App

No-DI glue for a game: services made by hand in an app scope and a scene scope, run at fixed places in Unity's frame and disposed last made first.

## Dependencies

- `com.rubickanov.log` — the `App` channel logs steps that throw

## Architecture

```
AppScope<TSelf, TApp, TScene>      MonoBehaviour, DontDestroyOnLoad, made at BeforeSceneLoad
├── Lifecycle  ── TApp services (+ extensions from assemblies the game does not reference)
└── SceneScope<TApp, TScene>       one per scene loaded in single mode, destroyed with it
    └── Lifecycle ── TScene services

FrameLoop (player loop, installed at SubsystemRegistration)
  TimeUpdate     GameTimeTick       before WaitForLastPresentationAndUpdateTime   TimeTick
  EarlyUpdate    GameStart          before ScriptRunDelayedStartupFrame           EarlyTick (all scopes), then Start (all scopes)
  FixedUpdate    GameFixedTick      before ScriptRunBehaviourFixedUpdate          FixedTick
                 GamePrePhysicsTick before PhysicsFixedUpdate                     PrePhysicsTick
                 GamePhysicsTick    before ScriptRunDelayedFixedFrameRate         PhysicsTick
  Update         GameTick           before ScriptRunBehaviourUpdate               Tick
  PreLateUpdate  GamePreLateTick    before ScriptRunBehaviourLateUpdate           PreLateTick
                 GameLateTick       last                                          LateTick
```

Each place runs the live lifecycles in the order they were made: the app's first, then the scene's.

## Core Concepts

- **Lifecycle** — one scope's list of what runs where. Services are made with `new` in the scope's services class and register their methods; nothing is found by reflection.
- **Own** — a service to dispose with the scope. Disposal goes last taken first, so a service goes before what it was made from.
- **Failures** — a step that throws is logged and the other steps go on, in disposal too. A tick that throws is still called every frame (a skipped tick would stop its service for good); only its first exception of each kind is logged. Disposing a lifecycle from inside a tick stops the rest of that tick.
- **Half-made scopes** — if the services' constructor throws, what it had owned is disposed at once. The app scope rethrows; a scene scope logs, stays empty (the scene is not set up twice) and calls `OnFailed`.

## Quick Start

```csharp
using Rubickanov.App;

public sealed class AppScope : AppScope<AppScope, AppServices, GameServices>
{
    protected override AppServices CreateServices(Lifecycle life) => new(transform, life);

    protected override SceneScope<AppServices, GameServices> CreateSceneScope(Scene scene, AppServices app) =>
        SceneScope<AppServices, GameServices>.Create<GameScope>(scene, app);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetApp() => ResetStatics();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BootApp() => Boot();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void JoinFirstScene() => JoinActiveScene();
}

public sealed class GameScope : SceneScope<AppServices, GameServices>
{
    protected override GameServices CreateServices(AppServices app, Scene scene, Lifecycle life) =>
        new(app, scene, life);
}
```

Unity does not run `RuntimeInitializeOnLoadMethod` on generic types, so the three calls live in the game's subclass. Each class sits in a file of its own name.

## Usage

### Services

```csharp
public sealed class GameServices
{
    public GameServices(AppServices app, Scene scene, Lifecycle life)
    {
        Water = life.Own(new Water(app.Audio));
        Run = life.Own(new CourseRun(Water, app.Input));

        life.Initialize(Water.Fill);
        life.Start(Run.Begin);                       // once, next frame
        life.Tick(Run.ReadInput, Water.Animate);     // before Update, in this order
        life.FixedTick(Run.Step);
        life.LateTick(Run.Camera.Follow);            // after every LateUpdate
    }

    public Water Water { get; }
    public CourseRun Run { get; }
}
```

### Game-only frame work

Work the package does not know goes in as steps of the app's lifecycle:

```csharp
// PhysX that is never stepped: its colliders follow the transforms at the frame's start and before LateUpdate.
life.EarlyTick(Physics.SyncTransforms);
life.PreLateTick(Physics.SyncTransforms);
```

### Extensions

An assembly the game does not reference (a dev console stripped from release) adds services on the app's lifecycle:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
private static void Install() =>
    AppScope.Extend((app, life) => life.Own(new ConsoleCommands(app)));
```

### Reaching the services

```csharp
AppServices app = AppScope.Instance.Services;   // null once the app has gone
GameServices game = AppScope.Instance.Game;     // the scene's; null between scenes or if they failed
```

### A way out of a broken scene

```csharp
protected override void OnFailed(AppServices app, Scene scene)
{
    if (scene.name != SceneFlow.MenuScene)
    {
        app.Party.LeaveGame();
    }
}
```

### Profiling

Every tick step gets a profiler marker made once when it is given: `FrameLoop.MarkerPrefix` (`"Game."`) + type + method, a lambda as `Type.Method~`. `FrameLoop.Names()` lists the live ones with the player loop system each runs under (empty in builds without the profiler).

## Design Decisions

- **No container.** Construction order is the code order in the services' constructors; a missing dependency is a compile error, not a runtime resolve failure.
- **Player loop systems, not MonoBehaviours.** A tick before `Update` and after every `LateUpdate` holds whatever execution orders the scripts carry.
- **A copy of the scope list per change.** A tick may make or end a scope; a running place goes over the list it started with.
- **Systems put in afresh at SubsystemRegistration.** With domain reload off, last session's systems and lifecycles are replaced, not doubled.
