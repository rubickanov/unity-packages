# Dev Console

In-game developer console with attribute-based command auto-discovery, autocomplete, subcommands, and persistent history.

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

    [ConsoleCommand("set_timescale", "Set time scale", "Debug")]
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

For custom types, register a parser — see [Custom Type Parsers](#custom-type-parsers).

### Return Values

- `void` — no output.
- `string` (or any non-null return) — printed to the console as an info message via `ToString()`.

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

Register a command with subcommands via `RegisterGroup` (or its alias `Group`). Each subcommand has its own handler and autocomplete providers:

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

Subcommand handlers receive args **after** the subcommand name: `inventory add apple 5` calls the handler with `["apple", "5"]`. Running the group with no args prints its usage; `help inventory` lists every subcommand.

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

The raw `Func<string[], string?>` overload remains available for arbitrary-arity handlers.

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
```

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
| `help` | List all commands, or `help <command>` for details |
| `clear` | Clear console output |
| `alias` / `unalias` / `alias_clear` | Manage and clear command aliases |
| `bind` / `unbind` / `binding_clear` | Manage and clear key bindings |
| `history` / `history_clear` | Inspect and clear persisted history (capped at 100 entries) |
| `exec <file>` | Run commands line-by-line from a file in `StreamingAssets/console/` or `persistentDataPath/console/` |
| `repeat <n> <cmd>` | Run a command N times |

Additional built-ins ship across categories: `fps`, `target_fps`, `vsync`, `memory`, `gc`, `timescale` (Performance); `resolution`, `fullscreen`, `quality` (Rendering); `scene`, `scene_list`, `find`, `inspect`, `count` (Scene); `quit`, `echo`, `version`, `sysinfo` (System); `log_unity`, `log_save` (Logging).

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

## Design Decisions

- **Two UI frontends** — **DevConsoleUIToolkit** (retained-mode, pooled elements) and **DevConsoleIMGUI** (immediate-mode, zero setup). Both honor `DevConsoleSettings`; pick whichever fits the project.
- **Static ConsoleLog** — decoupled from UI. Commands log via `ConsoleLog`; any frontend subscribes to `OnLogAdded`. Custom UIs can consume the same buffer.
- **Reflection-based discovery** — scans non-system assemblies for `[ConsoleCommand]` at startup, skipping `System.*`, `Unity.*`, `Mono.*`, `Microsoft.*`, `mscorlib`, `netstandard` prefixes for speed. Instance methods are not auto-discovered; bind them with `RegisterTarget(this)`.
- **Per-execution allocation in the reflection path** — `Execute` allocates a small `object?[]` for boxed arguments per call. Fine for a dev tool; not a per-frame hot path. Autocomplete (`GetSuggestions`) is allocation-free by contrast.
- **PlayerPrefs persistence** — aliases, history, and key bindings persist via PlayerPrefs. Simple and sufficient; history is capped at 100 entries.
- **Singleton frontends** — both frontends are singleton MonoBehaviours. Statics reset on `SubsystemRegistration` so domain-reload-disabled play sessions start clean.

## Related Packages

- [`com.rubickanov.devconsole.config`](../com.rubickanov.devconsole.config/) — auto-resolve `ConfigDatabase<T>` items by `Id` in command arguments.
- [`com.rubickanov.devconsole.netcode`](../com.rubickanov.devconsole.netcode/) — CS:GO-style command domains (Client / Server / Shared) and cheat protection.
</content>
</invoke>
