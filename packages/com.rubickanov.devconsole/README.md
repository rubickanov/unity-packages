# Dev Console

In-game developer console with attribute-based command auto-discovery, autocomplete, subcommands, and persistent history.

## Enabling

Every assembly in this package is constrained to the `ENABLE_CONSOLE` scripting define.
Without it nothing here compiles — not the runtime, not the editor window, not the tests —
so a console cannot reach a player build by being forgotten. Add `ENABLE_CONSOLE` to the
scripting defines of the build profiles that should have one (Project Settings → Player →
Scripting Define Symbols, or per profile in Build Profiles).

Two consequences worth knowing. Code that names types from this package has to live in an
assembly carrying the same constraint, or it will fail to compile when the define is absent.
And a console placed in a scene by hand becomes a missing script in builds without the
define, because scene data is not gated by defines — create it from a constrained assembly
instead.

## Dependencies

- `com.unity.inputsystem` — keyboard input for the toggle key and key bindings

Requires Unity 6000.0+.

## Architecture

```
[ConsoleCommand] attribute (on static / instance methods)
        │
        ▼
  CommandRegistry (singleton, reflection discovery + runtime registration)
        │
        ▼
  RegisteredCommand (metadata + handler + per-arg autocomplete providers)
        │
        ▼
  DevConsoleUIToolkit / DevConsoleIMGUI (MonoBehaviour frontends)
        │
        ▼
  ConsoleLog (static ring buffer, 1000 entries)
```

A frontend calls `CommandRegistry.Instance.Initialize()` on awake, which scans assemblies for `[ConsoleCommand]` static methods and registers the built-in commands. Commands write to the static `ConsoleLog`; frontends render it by subscribing to `OnLogAdded` / `OnCleared`.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.DevConsole.Runtime** | Yes | Commands, registry, autocomplete, console log, UI frontends |
| **Rubickanov.DevConsole.Editor** | Editor | Project Settings provider |

## Core Concepts

**CommandRegistry** — Singleton that discovers all `[ConsoleCommand]`-attributed static methods at startup via reflection, and supports runtime registration. Owns parsing, argument conversion, execution, and autocomplete.

**ConsoleLog** — Static ring buffer (1000 entries) with typed levels (`Info`, `Warning`, `Error`, `Success`, `Input`). Decoupled from any frontend; UIs subscribe to `OnLogAdded` / `OnCleared`.

**IAutoCompleteProvider** — Per-argument suggestion source. Built-in providers handle `bool` and `enum` parameters automatically; custom providers implement `GetSuggestions(string partial, List<string> results)`.

## Quick Start

1. Add a `UIDocument` component to a GameObject and assign the bundled `DevConsoleUI.uxml` (under the package's `Runtime/UI/`) as its source asset — or skip the UIDocument and use **DevConsoleIMGUI** for a zero-setup IMGUI console instead.
2. Attach the **DevConsoleUIToolkit** component to the same GameObject.
3. Press **`~`** (BackQuote) to toggle the console.

Commands are auto-discovered at startup — no manual registration needed.

## Usage

### Defining Commands

Add `[ConsoleCommand]` to any static method. Signature: `[ConsoleCommand(name, description = "", category = "General")]`.

```csharp
using Rubickanov.DevConsole;

public static class GameCommands
{
    [ConsoleCommand("heal", "Restore player health", "Cheats")]
    public static void Heal(int amount = 100)
    {
        ConsoleLog.LogSuccess($"Healed for {amount}");
    }

    [ConsoleCommand("time scale", "Set time scale", "Debug")]
    public static string SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        return $"Time scale set to {scale}";  // returned string is printed to the console
    }
}
```

### Instance Commands via RegisterTarget

`[ConsoleCommand]` works on instance methods too, but they are never auto-discovered. Register the owning object explicitly — useful for command classes resolved by a DI container:

```csharp
public class InventoryCommands
{
    private readonly IInventoryService _inventory;

    public InventoryCommands(IInventoryService inventory) => _inventory = inventory;

    public void Bind() => CommandRegistry.Instance.RegisterTarget(this);
    public void Unbind() => CommandRegistry.Instance.UnregisterTarget(this);

    [ConsoleCommand("inv.add", "Add item to inventory", "Cheats")]
    public string Add(string itemId, int amount = 1)
    {
        _inventory.Add(itemId, amount);
        return $"Added {amount}x {itemId}";
    }
}
```

`RegisterTarget` scans the target's public and non-public instance methods. Call `UnregisterTarget` when the owner is destroyed to drop stale handlers.

### Supported Parameter Types

Built-in: `string`, `int`, `long`, `ulong`, `float`, `bool`, any `enum`, and `Vector3` (parsed from `x,y,z`, spaces optional). Numbers parse with invariant culture. Parameters with default values are optional.

A nullable parameter (`int? width = null`) parses like its underlying type, with its parser and autocomplete, and stays `null` when left out. That is the usual shape of a get-or-set command: no argument shows the value, an argument sets it.

More arguments than parameters is an error, not silently dropped. A command that takes free text marks its last `string` parameter `[Remainder]`: it receives the rest of the line as it was typed, quotes included.

```csharp
[ConsoleCommand("say", "Broadcast a message", "Chat")]
public static void Say([Remainder] string message) { }   // say "hi" there → "hi" there
```

For custom types, register a parser — see [Custom Type Parsers](#custom-type-parsers).

### Return Values and Errors

- `void` — no output.
- `string` (or any non-null return) — printed to the console as an info message via `ToString()`.
- `throw new CommandException("…")` — fails the command with that message as the error and `Success = false`, so a chain or an `exec` file sees the failure. Works the same from attribute commands, `Register` handlers and group handlers. Any other exception is also reported as an error, with its message behind a short prefix.

### Autocomplete Providers

`bool` and `enum` parameters get autocomplete automatically. For custom suggestions, use `[AutoComplete(argumentIndex, providerType, params string[] providerArgs)]`:

```csharp
[ConsoleCommand("set_difficulty", "Set game difficulty", "Game")]
[AutoComplete(0, typeof(StaticListProvider), "easy", "normal", "hard", "nightmare")]
public static void SetDifficulty(string difficulty) { }
```

`providerArgs` are forwarded to the provider's constructor via `Activator.CreateInstance` — here `StaticListProvider("easy", "normal", …)`. Match the provider's ctor signature.

Built-in providers:

| Provider | Usage |
|----------|-------|
| **BoolAutoCompleteProvider** | Auto-applied to `bool` params (shared `Instance`) |
| **EnumAutoCompleteProvider** | Auto-applied to `enum` params |
| **StaticListProvider** | Fixed list of string options |
| **CommandLineProvider** | A whole nested command line (`repeat 3 <command...>`): completes command names, then that command's own arguments. Only as the last provider |

### Custom Autocomplete Provider

Implement **IAutoCompleteProvider**. `GetSuggestions` must append to the supplied list without allocating:

```csharp
public class PlayerNameProvider : IAutoCompleteProvider
{
    public string Hint => "<player>";

    public void GetSuggestions(string partial, List<string> results)
    {
        foreach (var name in GetAllPlayerNames())
            if (string.IsNullOrEmpty(partial) ||
                name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                results.Add(name);
    }
}
```

`Hint` is optional (defaults to null, falling back to the parameter name) and appears in the command's usage string.

### Custom Type Parsers

Register a parser for any type to use it directly as a command parameter. The delegate returns `(true, value)` on success or `(false, default)` on failure:

```csharp
CommandRegistry.Instance.RegisterParser<Player>(input =>
{
    var player = PlayerService.FindByName(input);
    return player != null ? (true, player) : (false, default);
});

[ConsoleCommand("kick", "Kick a player", "Admin")]
public static void Kick(Player player) { /* … */ }
```

Pair it with a default provider so every command using that type gets suggestions for free:

```csharp
CommandRegistry.Instance.RegisterDefaultProvider<Player>(new PlayerNameProvider());
```

`RegisterParser` and `RegisterDefaultProvider` return the registry for chaining. For ScriptableObject databases, the [`com.rubickanov.devconsole.config`](../com.rubickanov.devconsole.config/) extension registers parser and provider in a single call.

### Runtime Registration

Register commands from code without attributes. Two overloads: `Action<string[]>` (no output) and `Func<string[], string?>` (returns a message):

```csharp
CommandRegistry.Instance.Register("quit", _ => Application.Quit(), "Exit the game", "System");

CommandRegistry.Instance.Register("ping", args =>
{
    return $"Pong! Args: {string.Join(", ", args)}";
}, "Ping test", "Debug");
```

Remove a command with `Unregister(name)`.

### Command Groups (Subcommands)

A command name may be several words. The words before the last make a group, and every command whose name starts
with them is one of its subcommands, whichever assembly declared it:

```csharp
[ConsoleCommand("scene load", "Load a scene", "Scene")]
public static void Load(string name) { … }

// In the game's own assembly: `log` also holds the package's `log unity` and `log save`
[ConsoleCommand("log level", "Set a channel's level", "Logging")]
public static void SetLevel(string channel, LogLevel level) { … }
```

- `scene load Arena` runs `scene load` with `Arena`: the command with the most words the line starts with wins, so
  `scene` and `scene load` can both exist.
- A group typed alone, `log`, prints its subcommands; `log nope` is an unknown subcommand error.
- Autocomplete goes word by word: the first word lists commands and groups once each, `scene ` lists `list`, `load`,
  `reload` and whatever `scene` itself takes as an argument.
- `help log` lists the group; `help scene load` shows one subcommand.

Names use spaces, not underscores. Groups can nest: `net host lan` is fine.

`RegisterGroup` (or its alias `Group`) registers several subcommands with handlers at once, under a description for
the group. It adds to the group rather than replacing it. Each subcommand has its own handler and autocomplete providers:

```csharp
var fruitProvider = new FruitIdProvider(database);

CommandRegistry.Instance.RegisterGroup("inventory", "Manage inventory", "Cheats", group =>
{
    group.Add("add", args => AddFruit(args), "Add fruits", fruitProvider);
    group.Add("remove", args => RemoveFruit(args), "Remove fruits", fruitProvider);
    group.Add("clear", () => ClearInventory(), "Clear inventory");
    group.Add("list", _ => ListInventory(), "Show contents");
});
```

Autocomplete is context-aware per subcommand:
- `inventory ` → suggests `add`, `remove`, `clear`, `list`
- `inventory add ` → suggests fruit IDs (from `fruitProvider`)
- `inventory list ` → no suggestions

Subcommand handlers receive args **after** the subcommand name: `inventory add apple 5` calls the handler with `["apple", "5"]`. Running the group with no args prints its usage; `help inventory` lists every subcommand. `Unregister("inventory")` removes the whole group.

### Type-Safe Subcommands

`CommandGroupBuilder.Add` has generic overloads (`Add<T1>` … `Add<T1, T2, T3>`) that parse arguments through the registered parsers and default providers, so handlers receive typed values and arg providers are wired automatically:

```csharp
CommandRegistry.Instance.Group("inv", "Inventory", "Cheats", g =>
{
    g.Add<string, int>("add", (id, amount) => Inventory.Add(id, amount), "Add items");
    g.Add<string>("remove", id => Inventory.Remove(id), "Remove item");
    g.Add("clear", () => Inventory.Clear(), "Clear inventory");
});
```

The raw `Func<string[], string?>` overload remains available for arbitrary-arity handlers. A typed handler given the wrong number of arguments, or one that does not parse, fails with a usage error.

A subcommand whose tail is free text uses `AddWithRest`: arguments from `restFrom` on reach the handler as one string, as typed. `usage` replaces the generated hint in `help`:

```csharp
g.AddWithRest("say", 1, args => Chat.Send(args[0], args[1]), "Message a player",
    "<player> <message...>", playerProvider);
```

### Custom Frontends

A UI of its own runs input through `CommandRegistry.Instance.ExecuteAndLog(line)`, which echoes the line, runs it
and prints the result exactly as the bundled frontends do. For completion, `GetSuggestions(input, list)` suggests for
the last token of the last `;` statement, `DescribeSuggestion(input, suggestion)` gives the description of one that
names a command or a group, and `CommandRegistry.ApplySuggestion(input, suggestion)` puts the chosen one in its place,
quoting it when it contains spaces.

### Console Frontends

```csharp
// UI Toolkit frontend (instance methods)
DevConsoleUIToolkit.Instance.Show();
DevConsoleUIToolkit.Instance.Hide();
DevConsoleUIToolkit.Instance.Toggle();
bool open = DevConsoleUIToolkit.Instance.IsVisible;

// IMGUI frontend
DevConsoleIMGUI.Instance.Toggle();        // instance
DevConsoleIMGUI.Instance.SetOpen(true);   // instance
bool isOpen = DevConsoleIMGUI.IsOpen;     // static
DevConsoleIMGUI.Toggled += open => { };   // static event Action<bool>
```

`Instance` is null until the corresponding frontend exists in the scene.

The IMGUI log is selectable with the mouse: drag to select, double-click for a word, triple-click for a whole entry,
shift-click to extend. Ctrl+C (Cmd+C on macOS) copies the selection as plain text, without rich text tags. The log
draws colour tags only, so bold and size tags in messages show as plain text there.

### Logging

```csharp
ConsoleLog.Log("Player spawned");
ConsoleLog.LogWarning("Low health");
ConsoleLog.LogError("Connection failed");
ConsoleLog.LogSuccess("Level complete");
ConsoleLog.LogInput("heal 50");   // prefixed with "> "
```

Read or subscribe to the buffer:

```csharp
foreach (var entry in ConsoleLog.Entries)   // RingBufferView, oldest-first
    Debug.Log(entry.Message);

ConsoleLog.OnLogAdded += entry => Debug.Log(entry.Message);
ConsoleLog.OnCleared += () => Debug.Log("Console cleared");

long number = ConsoleLog.FirstNumber;   // number of Entries[0]; entry i is FirstNumber + i
int capacity = ConsoleLog.Capacity;     // the oldest entry is dropped past this
```

Every Unity log message (`Debug.Log*`, exceptions, engine messages) is copied into `ConsoleLog` as is, from any thread and from the first moment scripts run, before any frontend exists. Errors, exceptions and asserts carry their stack trace. Messages from other threads appear at the start of the next frame. `log unity false` stops the copying, `log unity true` resumes it.

Logs written to the console by a `ConsoleLog.OnLogAdded` subscriber through `Debug.Log` are not copied back, so a subscriber like the one above cannot loop.

### Pre-Execute Filter

`CommandRegistry.PreExecuteFilter` intercepts commands before execution. Return a non-null `ExecutionResult` to override the handler, or null to proceed:

```csharp
CommandRegistry.Instance.PreExecuteFilter = (cmd, args) =>
{
    if (cmd.Category == "Cheats" && !cheatsEnabled)
        return CommandRegistry.ExecutionResult.Error("Cheats are disabled.");
    return null;
};
```

### Built-in Commands

| Command | Description |
|---------|-------------|
| `help [topic]` | Everything, or one command, alias or category; any other word searches names and descriptions |
| `clear` | Clear console output |
| `alias list` / `set <name> <command...>` / `remove <name>` / `clear` | Short names for commands |
| `bind list` / `set <key> <command...>` / `remove <key>` / `clear` | Commands on key presses |
| `history list [N]` / `history clear` | Persisted input history (capped at 100 entries) |
| `exec <file>` | Run a file of commands, one per line; `#` starts a comment |
| `repeat <n> <command...>` | Run a command N times (at most 1000) |
| `toggle <command...> <a> <b> [c…]` | Run the command with the next of its values each call |
| `wait <frames>` | Delay the rest of a `;` chain or `exec` file |

The rest, by category:

- **Performance** — `fps` (average, slowest and fastest frame over the last second), `fps target [n]`, `vsync [n]`, `memory`, `gc`.
- **Time** — `timescale [scale]`, `pause` (stops time; again gives back the previous scale).
- **Rendering** — `resolution [w h [mode]]`, `resolution list`, `fullscreen [mode]`, `quality [name|index]`.
- **Scene** — `scene`, `scene list`, `scene load <name|index> [additive]`, `scene reload`, `inspect <name|path>` (every match, inactive ones too), `count [component] [includeInactive]`.
- **System** — `quit`, `echo <text...>`, `sysinfo` (application and Unity version, platform, hardware).
- **Logging** — `log unity [on]`, `log save`.

Commands that get or set a value show it when called without an argument.

### Chains, Aliases and Bindings

`;` separates commands on one line: `timescale 0.2; god true`. Each runs in turn, including after one that fails. A `;`
inside quotes is text.

`wait <frames>` stops a chain and runs what follows it that many frames later (counted at the start of `Update`):
`scene reload; wait 2; teleport spawn`. In an `exec` file it defers the rest of the file the same way. A
`PreExecuteFilter` that refuses `wait` makes the rest run at once instead, which is what the netcode bridge does for a
client's command.

An alias is a short name for a command line. Arguments given to the alias are appended, or placed with `$1`…`$9` (one
argument each, as typed) and `$*` (all of them). Quote the command when it contains `;`, so the whole chain becomes the
alias:

```
alias set slow timescale 0.2
alias set tp teleport $1 $2
alias set reset "scene reload; wait 2; god true"
```

Aliases complete like commands, and an alias without `$` or `;` completes its arguments like its target.

`bind set <key> <command...>` runs a command line when a key is pressed. The key is an Input System `Key` name,
optionally with modifiers that must match exactly: `bind set ctrl+shift+R scene reload`. Bindings do not fire while a
console is open. To keep them quiet during text entry of your own, set `CommandBindings.Suppress = () => chatOpen;`.

`toggle` cycles values on each call, which suits a binding: `bind set F1 toggle timescale 0 1`. Each distinct
argument list keeps its own position, starting with the first value.

### Config Files

Aliases and bindings are saved to `persistentDataPath/console/config.cfg` on every change and read back at startup.
The file is plain console commands (`alias set …`, `bind set …`), so it can be edited or copied between machines. The
console rewrites it, so lines of your own belong in `autoexec.cfg` beside it, which runs once when the console
initializes. Saves from before 2.0, kept in PlayerPrefs, move into `config.cfg` on the first change.

`exec <file>` looks in `persistentDataPath/console/` first, then `StreamingAssets/console/`; the `.cfg` extension may be
left out. Files may `exec` other files up to 8 levels deep. On WebGL, StreamingAssets cannot be read synchronously, so
only `persistentDataPath` files work there.

### Settings

**Project Settings > Dev Console** (persisted to `ProjectSettings/DevConsoleSettings.json`):

- **Use Built-in Toggle** — enable the built-in key toggle (default: on). Disable to drive visibility yourself via `Toggle()` / `SetOpen()`.
- **Toggle Key** — key to open/close the console (default: BackQuote).
- **Console Height** — fraction of screen height, range 0.1–0.9 (default: 0.4).

Both frontends read these settings.

### Stripping Commands from Release Builds

```csharp
#if DEVELOPMENT_BUILD || UNITY_EDITOR
[ConsoleCommand("god", "Toggle god mode", "Cheats")]
public static void GodMode() { }
#endif
```

## Startup commands from the command line

A build nobody can type into — a headless host, a machine started over ssh, a run that has to begin in a known
state — reaches the console through `-command`:

```
MyGame -batchmode -nographics -command "net host 7777"
MyGame -command "net profile lan" -command "net join 192.168.1.25 7777"
```

The flag may be repeated and the commands run in the order given. Each value is one console line, so the shell's
quoting is what separates a command from the next flag; the line itself is tokenized by the registry, which is why
`-command 'say "hello world"'` arrives intact. A `-command` with a blank or missing value is dropped, the way `exec`
drops a blank line in a file.

**The game decides when they run**, because only the game knows when its own command groups have registered. Call
`StartupCommands.Run(CommandRegistry.Instance)` from a hook that comes after everything has started — with VContainer
that is an `IPostStartable`, which runs after every `IStartable.Start()` of the container:

```csharp
public sealed class StartupCommandRunner : IPostStartable
{
    public void PostStart() => StartupCommands.Run(CommandRegistry.Instance);
}
```

The queue drains as it runs and resets once per process (`SubsystemRegistration`), so a scene reload that rebuilds
the game's scopes finds it empty and cannot start a second session on top of the first. `StartupCommands.Enqueue`
adds to the same queue by hand, for an intent formed while the game cannot act on it yet.

Every command is logged to `ConsoleLog` and, unlike `exec`, also to the player log with a `[DevConsole]` prefix: this
feature exists for a log file on another machine, and a command that is not registered is indistinguishable from a
typo from here, so failures are `LogError`. `StartupCommands.Parse` is pure and takes the arguments as a parameter,
so argument handling can be tested without starting a process.

## Design Decisions

- **Two UI frontends** — **DevConsoleUIToolkit** (retained-mode, pooled elements) and **DevConsoleIMGUI** (immediate-mode, zero setup). Both honor `DevConsoleSettings`; pick whichever fits the project.
- **Static ConsoleLog** — decoupled from UI. Commands log via `ConsoleLog`; any frontend subscribes to `OnLogAdded`. Custom UIs can consume the same buffer.
- **Unity logs forwarded by default** — subscribed on `SubsystemRegistration` through `logMessageReceivedThreaded`, not by a frontend, so startup logs are not lost. `ConsoleLog` stays main-thread only: other threads' messages wait in a queue drained in `PreUpdate`.
- **Reflection-based discovery** — scans non-system assemblies for `[ConsoleCommand]` at startup, skipping `System.*`, `Unity.*`, `Mono.*`, `Microsoft.*`, `mscorlib`, `netstandard` prefixes for speed. Instance methods are not auto-discovered; bind them with `RegisterTarget(this)`.
- **Per-execution allocation in the reflection path** — `Execute` allocates a small `object?[]` for boxed arguments per call. Fine for a dev tool; not a per-frame hot path. Autocomplete (`GetSuggestions`) allocates only the typed words and the group path.
- **Config file for aliases and bindings** — they are commands a person writes and wants to read, back up or share, so they live in `config.cfg` as console lines rather than JSON in PlayerPrefs. History stays in PlayerPrefs, capped at 100 entries.
- **Bindings restored without the `bind` command** — an `AfterSceneLoad` hook creates the polling object when saved bindings exist. Before 2.0 they loaded only once `bind` had been typed in that session.
- **Groups are name prefixes, not commands** — `scene load` is stored under its full name, and a group is only the
  words its commands share. So a package and a game can both add to `log`, and `PreExecuteFilter` sees the subcommand
  that runs, not the group. Before 3.0 a group was one command, registered only in code, and a second `Group` call
  with the same name replaced the first.
- **`wait` is a command, not syntax** — so the same `PreExecuteFilter` that guards everything else decides whether a deferred rest may run.
- **Singleton frontends** — both frontends are singleton MonoBehaviours. Statics reset on `SubsystemRegistration` so domain-reload-disabled play sessions start clean.

## Related Packages

- [`com.rubickanov.devconsole.config`](../com.rubickanov.devconsole.config/) — auto-resolve `ConfigDatabase<T>` items by `Id` in command arguments.
- [`com.rubickanov.devconsole.netcode`](../com.rubickanov.devconsole.netcode/) — CS:GO-style command domains (Client / Server / Shared) and cheat protection.
</content>
</invoke>
