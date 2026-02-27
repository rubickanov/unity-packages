# Dev Console

In-game developer console for Unity with attribute-based command auto-discovery, autocomplete, and persistent history.

## Features

- **Auto-discovery** — mark any static method with `[ConsoleCommand]` and it's registered automatically
- **Autocomplete** — tab-completion for command names and arguments, with pluggable providers
- **Persistent history** — command history saved across sessions via PlayerPrefs
- **Zero-alloc hot path** — autocomplete and input handling produce no GC allocations
- **UI Toolkit** — retained-mode UI with pooled elements, hidden when inactive = zero CPU cost

## Quick Start

1. Add a `UIDocument` component to any GameObject in your scene
2. Attach the `DevConsoleUI` component to the same GameObject
3. Press **`~`** (BackQuote) to toggle the console

The console auto-discovers commands at startup — no manual registration needed.

## Defining Commands

Add `[ConsoleCommand]` to any static method:

```csharp
using Rubickanov.DevConsole;

public static class MyCommands
{
    [ConsoleCommand("heal", "Restore player health", "Cheats")]
    public static void Heal(int amount = 100)
    {
        // your logic here
        ConsoleLog.LogSuccess($"Healed for {amount}");
    }
}
```

### Supported parameter types

`string`, `int`, `float`, `bool`, `enum`, `Vector3` (as `x,y,z`)

### Return values

- `void` — no output
- `string` — printed to console as info message

## Autocomplete Providers

Arguments get autocomplete automatically for `bool` and `enum` types. For custom suggestions, use `[AutoComplete]`:

```csharp
[ConsoleCommand("set_difficulty", "Set game difficulty", "Game")]
[AutoComplete(0, typeof(StaticListProvider), "easy", "normal", "hard", "nightmare")]
public static void SetDifficulty(string difficulty) { }
```

### Built-in providers

| Provider | Usage |
|---|---|
| `BoolAutoCompleteProvider` | Auto-applied to `bool` params |
| `EnumAutoCompleteProvider` | Auto-applied to `enum` params |
| `StaticListProvider` | Fixed list of string options |

### Custom provider

Implement `IAutoCompleteProvider`:

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

## Runtime Registration

Register commands from code without attributes:

```csharp
CommandRegistry.Instance.Register("quit", _ => Application.Quit(), "Exit the game", "System");
```

## Console API

```csharp
DevConsoleUI.Instance.Show();
DevConsoleUI.Instance.Hide();
DevConsoleUI.Instance.Toggle();
```

```csharp
ConsoleLog.Log("message");
ConsoleLog.LogWarning("warning");
ConsoleLog.LogError("error");
ConsoleLog.LogSuccess("success");
```

## Settings

**Project Settings > Dev Console**:

- **Toggle Key** — key to open/close the console (default: BackQuote)
- **Use Built-in Toggle** — disable to control via `DevConsoleUI.Instance.Toggle()` from your own input system
- **Console Height** — fraction of screen height (0.1 – 0.9)

## Stripping Commands from Release Builds

Wrap commands you don't want in release builds:

```csharp
#if DEVELOPMENT_BUILD || UNITY_EDITOR
[ConsoleCommand("god", "Toggle god mode", "Cheats")]
public static void GodMode() { }
#endif
```

## Built-in Commands

| Command | Description |
|---|---|
| `help` | List all commands, or `help <command>` for details |
| `clear` | Clear console output |

## Netcode Extension (optional)

For multiplayer projects using **Netcode for GameObjects**, the `DevConsole.Netcode` package adds CS:GO-style command domains and cheat protection. Without it, the console works in singleplayer mode as before.

See [`DevConsole.Netcode/`](../DevConsole.Netcode/) for details.

### Quick overview

- `[CommandDomain(CommandDomain.Server)]` — command runs only on server; clients send it via RPC
- `[CommandDomain(CommandDomain.Client)]` — command runs only locally, never sent to server
- `[CheatProtected]` — command requires `sv_cheats 1`
- Add `NetworkCommandBridge` component to a networked GameObject to activate

```csharp
[ConsoleCommand("sv_gravity", "Set gravity", "Server")]
[CommandDomain(CommandDomain.Server)]
public static string SetGravity(float value)
{
    Physics.gravity = new Vector3(0, -value, 0);
    return $"Gravity set to {value}";
}

[ConsoleCommand("god", "Toggle god mode", "Cheats")]
[CommandDomain(CommandDomain.Server)]
[CheatProtected]
public static void GodMode() { /* ... */ }

[ConsoleCommand("cl_showfps", "Toggle FPS counter", "Client")]
[CommandDomain(CommandDomain.Client)]
public static void ShowFPS() { /* ... */ }
```

## Package Structure

```
DevConsole/
├── package.json
├── README.md
├── Editor/
│   └── DevConsoleSettingsProvider.cs
└── Runtime/
    ├── Attributes/
    ├── AutoComplete/
    ├── Core/
    ├── Examples/
    ├── Resources/UI/
    └── UI/
```
