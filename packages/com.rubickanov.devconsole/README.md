# Dev Console

In-game developer console: an IMGUI window with attribute-discovered commands, word-by-word autocomplete, subcommand groups, aliases, key bindings and a history kept in plain files.

## Dependencies

- `com.unity.inputsystem` — keyboard input for the toggle key and key bindings
- `com.rubickanov.log` (optional) — when present, the `Rubickanov.DevConsole.Log` assembly adds `log channels` and `log level`

Requires Unity 6000.0+.

## Architecture

```
[ConsoleCommand] static methods ──► CommandRegistry.Instance ◄── Register / RegisterTarget / Group
                                     ├── Aliases, Bindings   ── persistentDataPath/console/config.cfg
                                     └── History             ── persistentDataPath/console/history.txt
first frame (ConsoleStartup): discover ─► autoexec.cfg ─► -command lines
DevConsoleWindow ──ExecuteAndLog──► CommandRegistry ──► ConsoleLog (ring buffer, 1000 entries)
CommandBindings  ──ExecuteAndLog──┘          Unity log ──► ConsoleLog
```

The package creates the window before the first scene loads and starts the console on the first frame. Commands
write to the static `ConsoleLog`; the window draws it, and every Unity log message is copied into it.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.DevConsole.Runtime** | Yes | Registry, built-in commands, autocomplete, log, window, bindings |
| **Rubickanov.DevConsole.Log** | Yes | `log channels` and `log level` over `com.rubickanov.log`; compiled only when that package is installed |
| **Rubickanov.DevConsole.Editor** | Editor | Project Settings page, settings file |

Every assembly, tests included, is constrained to the `ENABLE_CONSOLE` scripting define: without it nothing here
compiles, so a console cannot reach a player build by being forgotten. Code that names types from this package has to
live in an assembly with the same constraint.

## Core Concepts

**CommandRegistry** — The console itself: the commands (discovered and registered), their parsing and execution,
autocomplete, and the console's aliases, bindings and history. `CommandRegistry.Instance` is the one the window uses.

**ConsoleLog** — Static ring buffer (1000 entries) with typed levels (`Info`, `Warning`, `Error`, `Success`, `Input`).
The window renders it; any other UI can subscribe to `OnLogAdded` / `OnCleared`.

**IAutoCompleteProvider** — Per-argument suggestion source. `bool` and `enum` parameters get one automatically.

**The first frame** — The console starts once per process on the first frame's Update, before any
`MonoBehaviour.Update`: after every `Awake`, `OnEnable` and `Start` of the first scene and after VContainer's
`IStartable` and `IPostStartable`, which run in EarlyUpdate. It discovers the commands, runs `autoexec.cfg`, then the
`-command` lines. A discovered command replaces a same-named one registered before that.

## Quick Start

1. Add `ENABLE_CONSOLE` to the scripting defines of the build profiles that should have a console (Project Settings →
   Player → Scripting Define Symbols, or per profile in Build Profiles).
2. Press **`~`** (BackQuote) in Play mode. The window exists already; nothing goes in a scene.
3. Add a command:

```csharp
public static class CheatCommands
{
    [ConsoleCommand("heal", "Restore player health", "Cheats")]
    public static string Heal(int amount = 100) => $"Healed for {amount}";
}
```

In the input line, **Enter** runs the line as typed, **Tab** completes the highlighted suggestion, **Up** / **Down**
move through the suggestions (or the history when there are none), and **Esc** hides the suggestions, then closes.

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

    public void Initialize() => CommandRegistry.Instance.RegisterTarget(this);
    public void Dispose() => CommandRegistry.Instance.UnregisterTarget(this);

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

`RegisterParser` and `RegisterDefaultProvider` return the registry for chaining.

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
[ConsoleCommand("log upload", "Send the player log to the crash server", "Logging")]
public static void Upload(string note = "") { … }
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

### Opening and Closing the Window

```csharp
bool open = DevConsoleWindow.IsOpen;              // static: a window exists and is open
DevConsoleWindow.Toggled += open => { };          // static event Action<bool>
DevConsoleWindow.Instance?.Toggle();              // null until the window exists
DevConsoleWindow.Instance?.SetOpen(false);
```

`Toggled` is raised with `false` also when the window is destroyed while open, so input blocked on it always comes
back. With **Create Window** off in the settings, `DevConsoleWindow.Create()` creates it (once; later calls return it).

The log is selectable with the mouse: drag to select, double-click for a word, triple-click for a whole entry,
shift-click to extend. Ctrl+C (Cmd+C on macOS) copies the selection as plain text, without rich text tags. The log
draws colour tags only, so bold and size tags in messages show as plain text there.

### Custom Frontends

A UI of its own runs input through `CommandRegistry.Instance.ExecuteAndLog(line)`, which echoes the line, runs it
and prints the result exactly as the window does. For completion, `GetSuggestions(input, list)` suggests for
the last token of the last `;` statement, `DescribeSuggestion(input, suggestion)` gives the description of one that
names a command or a group, and `CommandRegistry.ApplySuggestion(input, suggestion)` puts the chosen one in its place,
quoting it when it contains spaces.

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
| `history list [N]` / `history clear` | Input history (the last 100 lines) |
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
- **Logging** — `log unity [on]`, `log save`; with `com.rubickanov.log`, `log channels` and `log level <channel|*> [level]`.

Commands that get or set a value show it when called without an argument.

### Chains, Aliases and Bindings

`;` separates commands on one line: `timescale 0.2; god true`. Each runs in turn, including after one that fails. A `;`
inside quotes is text.

`wait <frames>` stops a chain and runs what follows it that many frames later (counted at the start of `Update`):
`scene reload; wait 2; teleport spawn`. In an `exec` file it defers the rest of the file the same way. A
`PreExecuteFilter` that refuses `wait` makes the rest run at once instead, as a
networked console would for a client's command.

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
optionally with modifiers that must match exactly: `bind set ctrl+shift+R scene reload`. Bindings do not fire while the
console is open. To keep them quiet during text entry of your own, set `CommandBindings.Suppress = () => chatOpen;`.

`toggle` cycles values on each call, which suits a binding: `bind set F1 toggle timescale 0 1`. Each distinct
argument list keeps its own position, starting with the first value.

### Files of the Console

Everything the console keeps is in `persistentDataPath/console/`, as text:

- `config.cfg` — aliases and bindings as console commands (`alias set …`, `bind set …`), rewritten on every change and
  read back at startup, so it can be edited or copied between machines. Saves from before 2.0, in PlayerPrefs, move
  into it on the first change.
- `history.txt` — the last 100 lines typed, one per line. A 3.x history, in PlayerPrefs, moves into it on the first
  new line.
- `autoexec.cfg` — lines of your own, run on the first frame. The console never writes it.

`exec <file>` looks in `persistentDataPath/console/` first, then `StreamingAssets/console/`; the `.cfg` extension may be
left out. Files may `exec` other files up to 8 levels deep. On WebGL, StreamingAssets cannot be read synchronously, so
only `persistentDataPath` files work there.

### Settings

**Project Settings > Dev Console**, saved to `Assets/Resources/DevConsoleSettings.json` (any `Resources` folder works),
so builds carry them; without the file every setting has its default. A 3.x `ProjectSettings/DevConsoleSettings.json`
is moved there when the editor first loads 4.0.

- **Use Built-in Toggle** — open and close with the toggle key (default: on). Off: call `Toggle()` / `SetOpen()`.
- **Toggle Key** — key to open/close the console (default: BackQuote).
- **Console Height** — fraction of screen height, 0.1–0.9 (default: 0.4).
- **Create Window** — the package creates the window before the first scene loads (default: on).
- **Run Startup Commands** — the package runs the `-command` lines on the first frame (default: on).

### Startup Commands from the Command Line

A build nobody can type into — a headless host, a machine started over ssh, a run that has to begin in a known
state — reaches the console through `-command`:

```text
MyGame -batchmode -nographics -command "net host 7777"
MyGame -command "net profile lan" -command "net join 192.168.1.25 7777"
```

The flag may be repeated and the commands run in the order given, on the first frame after `autoexec.cfg`. Each value
is one console line, so the shell's quoting separates a command from the next flag; `-command 'say "hello world"'`
arrives intact. A blank value is dropped.

A game whose commands register later, with a scene it loads, turns **Run Startup Commands** off and runs them itself
once they are there:

```csharp
public sealed class SessionStartupCommands : IPostStartable   // in the gameplay scene's scope
{
    public void PostStart() => StartupCommands.Run(CommandRegistry.Instance);
}
```

The queue drains as it runs and resets once per process, so a scene reload that rebuilds the scope finds it empty.
`StartupCommands.Enqueue` adds to it by hand. Every command is logged to `ConsoleLog` and also to the player log with a
`[DevConsole]` prefix, failures as errors, since a log file on another machine is all there is to read.
`StartupCommands.Parse` is pure, for testing argument handling.

### Stripping Commands from Release Builds

```csharp
#if DEVELOPMENT_BUILD || UNITY_EDITOR
[ConsoleCommand("god", "Toggle god mode", "Cheats")]
public static void GodMode() { }
#endif
```

## Integration

The game keeps only what is its own, such as blocking its input while the console is open. With VContainer:

```csharp
public sealed class ConsoleInputBlock : IInitializable, IDisposable
{
    private readonly InputBlocker _blocker;

    public ConsoleInputBlock(InputBlocker blocker) => _blocker = blocker;

    public void Initialize() => DevConsoleWindow.Toggled += OnToggled;

    public void Dispose()
    {
        DevConsoleWindow.Toggled -= OnToggled;
        _blocker.Set(this, false);
    }

    private void OnToggled(bool open) => _blocker.Set(this, open);
}
```

Command classes resolved by the container register themselves with `RegisterTarget(this)` in `Initialize` and
`UnregisterTarget(this)` in `Dispose`; they are there for autoexec and `-command` as long as they register before the
first frame.

## Design Decisions

- **One window, IMGUI** — zero setup and nothing in a scene. The UI Toolkit frontend was removed in 4.0: two frontends
  meant two open states, and a game blocking its input on one missed the other.
- **Created by the package** — a window placed in a scene becomes a missing script in a build without
  `ENABLE_CONSOLE`; created from a constrained assembly at `BeforeSceneLoad`, it cannot.
- **Started on the first frame** — late enough for a game's own registrations in `Awake`, `Start` and VContainer's
  startables, early enough that nothing has been typed. Before 4.0, autoexec ran in the window's `Awake`, before them.
- **Static ConsoleLog** — decoupled from the window; commands log through it, any UI can subscribe.
- **Unity logs forwarded by default** — subscribed at `SubsystemRegistration` through `logMessageReceivedThreaded`, so
  startup logs are not lost. `ConsoleLog` stays main-thread only: other threads' messages wait in a queue drained in
  `PreUpdate`.
- **Quiet in the game's log** — the console's routine news (commands registered, autoexec ran) goes to the console
  only; only problems, and the `-command` lines, reach Unity's log.
- **Discovery reads only assemblies that reference the console** — only they can carry `[ConsoleCommand]`, and
  checking references loads no types (24 ms → 2 ms in the sandbox editor). Not `TypeCache`, which exists only in the
  editor and would make the editor and builds find commands differently.
- **Arguments parse with TryParse** — a typo is the common case; an exception per typo is slow and stops a debugger
  that breaks on throw.
- **Aliases, bindings and history belong to the registry** — not singletons of their own, so a fresh registry is a
  fresh console and the history commands work without a window.
- **Plain files in one folder** — aliases, bindings and history are text a person reads, backs up or shares, in
  `persistentDataPath/console/`. Not PlayerPrefs: the registry on Windows, shared with the game's own prefs and wiped
  by its `PlayerPrefs.DeleteAll`.
- **Settings as JSON in Resources** — builds carry them; a TextAsset rather than a ScriptableObject, so a release build
  without the console carries a few bytes of text and not an object with a missing script.
- **Groups are name prefixes, not commands** — `scene load` is stored under its full name, and a group is only the
  words its commands share. So a package and a game can both add to `log`, and `PreExecuteFilter` sees the subcommand
  that runs, not the group.
- **`wait` is a command, not syntax** — so the same `PreExecuteFilter` that guards everything else decides whether a
  deferred rest may run.
- **Per-execution allocation in the reflection path** — `Execute` allocates a small `object?[]` for boxed arguments per
  call. Fine for a dev tool; not a per-frame hot path. Autocomplete allocates only the typed words and the group path.
