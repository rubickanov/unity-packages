# Dev Console

In-game developer console with attribute-based command auto-discovery, autocomplete, and persistent history.

## Dependencies

- `com.unity.inputsystem` — keyboard input for toggle key

## Architecture

```
[ConsoleCommand] attribute (on static methods)
        |
        v
  CommandRegistry (singleton, reflection discovery)
        |
        v
  RegisteredCommand (metadata + handler + autocomplete providers)
        |
        v
  DevConsoleUIToolkit / DevConsoleIMGUI (MonoBehaviour frontends)
        |
        v
  ConsoleLog (static ring buffer, 1000 entries)
```

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **DevConsole.Runtime** | Yes | Commands, autocomplete, console log, UI frontends |
| **DevConsole.Editor** | Editor | Project Settings provider |

## Core Concepts

**CommandRegistry** — Singleton that discovers all `[ConsoleCommand]`-attributed static methods at startup via reflection. Also supports runtime registration. Handles parsing, argument conversion, and execution.

**ConsoleLog** — Static ring buffer (1000 entries) with typed log levels (`Info`, `Warning`, `Error`, `Success`, `Input`). UI frontends subscribe to `OnLogAdded` / `OnCleared` events.

**IAutoCompleteProvider** — Interface for argument autocomplete. Built-in providers handle `bool` and `enum` types automatically. Custom providers implement `GetSuggestions(string partial, List<string> results)`.

## Quick Start

1. Add a `UIDocument` component to a GameObject and assign the bundled `DevConsoleUI.uxml` (under the package's `Runtime/UI/`) as the source asset (or skip this step and use **DevConsoleIMGUI** for an IMGUI-based console).
2. Attach the **DevConsoleUIToolkit** component to the same GameObject.
3. Press **`~`** (BackQuote) to toggle the console.

The console auto-discovers commands at startup -- no manual registration needed.

## Usage

### Defining Commands

Add `[ConsoleCommand]` to any static method:

```csharp
using Rubickanov.DevConsole;

public static class GameCommands
{
    [ConsoleCommand("heal", "Restore player health", "Cheats")]
    public static void Heal(int amount = 100)
    {
        // your logic here
        ConsoleLog.LogSuccess($"Healed for {amount}");
    }

    [ConsoleCommand("set_timescale", "Set time scale", "Debug")]
    public static string SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        return $"Time scale set to {scale}";
    }
}
```

### Instance Commands via RegisterTarget

`[ConsoleCommand]` also works on instance methods, but those are never auto-discovered. Register the target object explicitly — useful for command classes resolved by your DI container:

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

Call `UnregisterTarget` when the owning object is destroyed to avoid stale handlers.

### Supported Parameter Types

Built-in: `string`, `int`, `long`, `ulong`, `float`, `bool`, any `enum`, `Vector3` (as `x,y,z` with optional spaces).

For custom types, register a parser — see [Custom Type Parsers](#custom-type-parsers) below.

### Return Values

- `void` — no output
- `string` — printed to console as info message

### Autocomplete Providers

`bool` and `enum` parameters get autocomplete automatically. For custom suggestions, use `[AutoComplete]`:

```csharp
[ConsoleCommand("set_difficulty", "Set game difficulty", "Game")]
[AutoComplete(0, typeof(StaticListProvider), "easy", "normal", "hard", "nightmare")]
public static void SetDifficulty(string difficulty) { }
```

`AutoComplete`'s extra arguments after `providerType` are forwarded to the provider's constructor via `Activator.CreateInstance` — `StaticListProvider("easy", "normal", …)` in the example above. Match the provider's ctor signature.

Built-in providers:

| Provider | Usage |
|----------|-------|
| **BoolAutoCompleteProvider** | Auto-applied to `bool` params |
| **EnumAutoCompleteProvider** | Auto-applied to `enum` params |
| **StaticListProvider** | Fixed list of string options |

### Custom Autocomplete Provider

Implement **IAutoCompleteProvider**:

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

### Custom Type Parsers

Register a parser for any type to use it directly as a command parameter. The parser delegate returns `(true, value)` on success or `(false, default)` on failure:

```csharp
CommandRegistry.Instance.RegisterParser<Player>(input =>
{
    var player = PlayerService.FindByName(input);
    return player != null ? (true, player) : (false, default);
});

[ConsoleCommand("kick", "Kick a player", "Admin")]
public static void Kick(Player player) { /* … */ }
```

Pair it with a default autocomplete provider so every command using that type gets suggestions for free:

```csharp
CommandRegistry.Instance.RegisterDefaultProvider<Player>(new PlayerNameProvider());
```

For ScriptableObject databases, use the [`com.rubickanov.devconsole.config`](../com.rubickanov.devconsole.config/) extension — it registers parser + provider in a single call.

### Type-Safe Subcommand Builder

`CommandGroupBuilder.Add<T1...T3>(...)` overloads parse arguments via the registered parsers + default providers, so handlers receive typed values:

```csharp
CommandRegistry.Instance.Group("inv", "Inventory", "Cheats", g =>
{
    g.Add<string, int>("add", (id, amount) => Inventory.Add(id, amount), "Add items");
    g.Add<string>("remove", id => Inventory.Remove(id), "Remove item");
    g.Add("clear", () => Inventory.Clear(), "Clear inventory");
});
```

`Group` is a shorthand alias for `RegisterGroup`. The raw `Func<string[], string?>` overload is still available for arbitrary-arity handlers.

### Runtime Registration

Register commands from code without attributes:

```csharp
CommandRegistry.Instance.Register("quit", _ => Application.Quit(), "Exit the game", "System");
```

The `Register` overload with `Func<string[], string>` handler allows returning a message:

```csharp
CommandRegistry.Instance.Register("ping", args =>
{
    return $"Pong! Args: {string.Join(", ", args)}";
}, "Ping test", "Debug");
```

### Command Groups (Subcommands)

Register commands with subcommands using `RegisterGroup`. Each subcommand gets its own handler and autocomplete providers:

```csharp
var fruitProvider = new FruitIdProvider(database);

CommandRegistry.Instance.RegisterGroup("inventory", "Manage inventory", "Cheats", group =>
{
    group.Add("add", args => AddFruit(args), "Add fruits", fruitProvider);
    group.Add("remove", args => RemoveFruit(args), "Remove fruits", fruitProvider);
    group.Add("clear", _ => ClearInventory(), "Clear inventory");
    group.Add("list", _ => ListInventory(), "Show contents");
});
```

Autocomplete is context-aware per subcommand:
- `inventory ` → suggests `add`, `remove`, `clear`, `list`
- `inventory add ` → suggests fruit IDs (from `fruitProvider`)
- `inventory list ` → no suggestions

Subcommand handlers receive args **after** the subcommand name: `inventory add apple 5` calls handler with `["apple", "5"]`.

`help inventory` shows all subcommands with descriptions.

### Console API

```csharp
// UI Toolkit frontend
DevConsoleUIToolkit.Instance.Show();
DevConsoleUIToolkit.Instance.Hide();
DevConsoleUIToolkit.Instance.Toggle();

// IMGUI frontend
DevConsoleIMGUI.Instance  // same API pattern
DevConsoleIMGUI.Toggled   // event Action<bool>
DevConsoleIMGUI.IsOpen    // static bool
```

### Logging

```csharp
ConsoleLog.Log("Player spawned");
ConsoleLog.LogWarning("Low health");
ConsoleLog.LogError("Connection failed");
ConsoleLog.LogSuccess("Level complete");
```

Subscribe to log events:

```csharp
ConsoleLog.OnLogAdded += entry => Debug.Log(entry.Message);
ConsoleLog.OnCleared += () => Debug.Log("Console cleared");
```

### Pre-Execute Filter

**CommandRegistry** exposes `PreExecuteFilter` to intercept commands before execution. Return a non-null `ExecutionResult` to override the default handler:

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
| `history` / `history_clear` | Inspect and clear persisted history |
| `exec <file>` | Run commands line-by-line from a file in `StreamingAssets/console/` or `persistentDataPath/console/` |
| `repeat <n> <cmd>` | Run a command N times |

### Settings

**Project Settings > Dev Console** (values persisted to `ProjectSettings/DevConsoleSettings.json`):

- **Toggle Key** — key to open/close the console (default: BackQuote)
- **Use Built-in Toggle** — disable to control via `DevConsoleUIToolkit.Instance.Toggle()` from your own input system
- **Console Height** — fraction of screen height (0.1 -- 0.9)

### Stripping Commands from Release Builds

```csharp
#if DEVELOPMENT_BUILD || UNITY_EDITOR
[ConsoleCommand("god", "Toggle god mode", "Cheats")]
public static void GodMode() { }
#endif
```

## Design Decisions

- **Two UI frontends** — **DevConsoleUIToolkit** (retained-mode, pooled elements) and **DevConsoleIMGUI** (immediate-mode, zero setup). Pick the one that fits your project.
- **Static ConsoleLog** — decoupled from UI. Commands log via `ConsoleLog`, any frontend subscribes to `OnLogAdded`. Custom UIs can consume the same buffer.
- **Reflection-based discovery** — scans non-system assemblies for `[ConsoleCommand]` attributes at startup. Skips `System.*`, `Unity.*`, `Mono.*` prefixes for performance. Instance methods are not auto-discovered; use `RegisterTarget(this)` to bind them.
- **Per-execution allocation in reflection path** — `ExecuteReflection` allocates a small `object?[]` for boxed arguments per call. Acceptable for a dev tool; not on a per-frame hot path.
- **PlayerPrefs persistence** — aliases, history, and key bindings persist via PlayerPrefs. Simple and sufficient for a dev tool. History is capped at 100 entries.
- **Singleton pattern** — both frontends use singleton MonoBehaviour with `DontDestroyOnLoad`. Only one console instance per scene.

## Related Packages

- [`com.rubickanov.devconsole.config`](../com.rubickanov.devconsole.config/) — auto-resolve `ConfigDatabase<T>` items by `Id` in commands.
- [`com.rubickanov.devconsole.netcode`](../com.rubickanov.devconsole.netcode/) — CS:GO-style command domains (Client / Server / Shared) and cheat protection.
