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

1. Add a `UIDocument` component to a GameObject (or use **DevConsoleIMGUI** for an IMGUI-based console).
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

### Supported Parameter Types

`string`, `int`, `float`, `bool`, `enum`, `Vector3` (as `x,y,z`)

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

### Settings

**Project Settings > Dev Console**:

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
- **Reflection-based discovery** — scans non-system assemblies for `[ConsoleCommand]` attributes at startup. Skips `System.*`, `Unity.*`, `Mono.*` prefixes for performance.
- **PlayerPrefs history** — command history persists across sessions via PlayerPrefs. Simple and sufficient for a dev tool.
- **Singleton pattern** — both frontends use singleton MonoBehaviour with `DontDestroyOnLoad`. Only one console instance per scene.

## File Structure

```
com.rubickanov.devconsole/
├── Runtime/
│   ├── Attributes/
│   │   ├── ConsoleCommandAttribute.cs
│   │   └── AutoCompleteAttribute.cs
│   ├── AutoComplete/
│   │   ├── IAutoCompleteProvider.cs
│   │   └── BuiltInProviders.cs
│   ├── Core/
│   │   ├── CommandRegistry.cs
│   │   ├── RegisteredCommand.cs
│   │   ├── SubcommandDefinition.cs
│   │   ├── CommandGroupBuilder.cs
│   │   ├── ConsoleLog.cs
│   │   ├── CommandHistory.cs
│   │   └── DevConsoleSettings.cs
│   └── UI/
│       ├── DevConsoleUIToolkit.cs
│       ├── DevConsoleIMGUI.cs
│       ├── DevConsoleUI.uxml
│       └── DevConsoleUI.uss
└── Editor/
    └── DevConsoleSettingsProvider.cs
```
