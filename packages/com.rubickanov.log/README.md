# Log

Named log channels over Unity's log, with per-channel levels and messages built only when a channel writes them.

## Dependencies

None.

Code that logs interpolated strings needs C# 10: put a `csc.rsp` with `-langVersion:10` next to its asmdef.

## Architecture

```
LogChannel.Get("Course") ──► LogChannel (level from -log or code)
        │  Info($"...") → InfoMessage handler skips holes when silent
        ▼
    LogWriter ──► Debug.unityLogger
                   ├── Editor Console: [Course] in a per-channel colour
                   └── Player.log: time, frame, level letter, no stack trace for Info/Warn
```

Everything ends up in Unity's log, so the Console, click-through to the caller, context pinging and `Player.log` work as with `Debug.Log`.

## Quick Start

```csharp
using Rubickanov.Log;

public sealed class CheckpointTracker
{
    private static readonly LogChannel Log = LogChannel.Get("Course");

    public void Reach(Checkpoint checkpoint, float time)
    {
        Log.Info($"Reached {checkpoint} at {time:F2}s", checkpoint);
    }
}
```

## Usage

### Channels

Channels are shared by name, case-insensitively: two files that ask for `"Course"` write to the same channel. Declare one as a static field where it is used.

```csharp
private static readonly LogChannel Log = LogChannel.Get("Course");

Log.Verbose($"Piston phase {phase:F3}");
Log.Info($"Player respawned at {checkpoint}");
Log.Warn($"Checkpoint list is empty after {reloads} reloads");
Log.Error($"Finish line not found in {level.Name}", this);
Log.Exception(exception, this);
```

`Verbose` calls are compiled out unless `UNITY_EDITOR`, `DEBUG` or `LOG_VERBOSE` is defined. A channel writes `Info` and up by default.

`Verbose`, `Info`, `Warn` and `Error` take interpolated strings (a plain `string` works too). Holes format numbers with the invariant culture and Unity objects by name (`null` if destroyed). When the channel would not write the message, holes are not evaluated at all.

`Exception` writes the exception as Unity prints it (type, message, clickable stack trace, still an exception for `Application.logMessageReceived`) behind the channel's tag and scope. It is written whatever the channel's level, `Off` included: an exception is never hidden.

### Levels

From the command line, with `*` for every channel not named:

```
Game.exe -log Course=Verbose,UI=Off,*=Warn
```

From code:

```csharp
LogChannel.Get("Course").Level = LogLevel.Verbose;

foreach (LogChannel channel in LogChannel.All)
{
    channel.Level = LogLevel.Warn;
}
```

`LogChannel.All` is a snapshot of the channels created so far, safe to read from any thread. A channel appears once the class declaring it is first used. Levels reset from the command line on each play session, including with domain reload off.

### Scope

`LogChannel.Scope` goes in front of every message of every channel, to tell apart who is speaking when several
processes write to one log, such as the host and the clients of a networked game:

```csharp
LogChannel.Scope = network.IsHost ? "Host" : $"Client {network.LocalClientId}";
// [Net] [Host] Player 2 joined
```

It is null by default and again on each play session.

### Following a Channel

`LevelChanged` lets code with its own log, such as a third-party library, follow a channel's level:

```csharp
LogChannel net = LogChannel.Get("Net");
net.LevelChanged += channel => transport.LogLevel = channel.Writes(LogLevel.Verbose) ? Verbosity.All : Verbosity.Errors;
```

## Design Decisions

- **Over `Debug.unityLogger`, not a separate sink.** Keeps Console click-through and `Player.log` intact. For file rotation and structured logging see `com.rubickanov.logging`.
- **Interpolated string handlers instead of templates.** A silent channel costs one level check; no boxing, no format string parsing.
- **No stack traces for `Log`/`Warning` in players.** They made up most of `Player.log`; errors and exceptions keep theirs.
