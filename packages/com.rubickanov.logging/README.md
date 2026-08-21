# Logging

ZLogger-based logging factory with file rotation, platform-specific outputs, and Unity log interception.

## Dependencies

> `ZLogger` comes from a git URL, not from UPM — UPM will not pull it in for you. See [Third-party dependencies](https://github.com/rubickanov-org/unity-packages#third-party-dependencies).

- `ZLogger.Unity` — structured logging providers (file, Unity console, server console) built on Microsoft.Extensions.Logging

## Architecture

```
LoggerFactoryBuilder.Build ──► ILoggerFactory (MEL)
                                     │
                           ┌─────────┼─────────┐
                           ▼         ▼         ▼
                      ZLoggerFile  Unity    Console
                      (optional)   Debug    (UNITY_SERVER)
                                   (editor)
```

**LoggerFactoryBuilder** creates a Microsoft.Extensions.Logging `ILoggerFactory` wired with ZLogger providers. Active outputs are chosen by platform: file logging (optional, any build), Unity Debug log (editor), console (dedicated server). All providers share one plain-text formatter: `timestamp [level] category | message`.

**LoggingSettings** is a preloaded `ScriptableObject` holding minimum level, file rotation, and naming settings. Because it is preloaded, `LoggingSettings.Instance` is available before any DI container is built.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.Logging.Runtime** | Yes | `LoggerFactoryBuilder`, `LoggingSettings`, `UnityLogInterceptor` |
| **Rubickanov.Logging.Editor** | Editor | Project Settings provider, auto-creates and preloads the settings asset |

## Quick Start

1. Open **Edit > Project Settings > Logging** — the settings asset is created and registered as a preloaded asset automatically.
2. Build the logger factory at application startup:

```csharp
// File logging is on by default in builds; in the editor it follows the
// Project Settings toggle (off unless enabled).
bool enableFile = true;
#if UNITY_EDITOR
enableFile = UnityEditor.EditorPrefs.GetBool("Logging.EnableFileInEditor", false);
#endif

ILoggerFactory loggerFactory = LoggerFactoryBuilder.Build(LoggingSettings.Instance, enableFile);
```

3. Register the factory in your LifetimeScope:

```csharp
builder.RegisterInstance(loggerFactory).As<ILoggerFactory>();
```

## Usage

### Creating Loggers

`ILoggerFactory` produces category-scoped MEL loggers. Use structured message templates — ZLogger captures the named values without allocating formatted strings until written.

```csharp
ILogger<NetworkSession> logger = loggerFactory.CreateLogger<NetworkSession>();

logger.LogInformation("Connected to {Host}:{Port}", host, port);
logger.LogWarning("Packet loss {Loss}%", packetLoss);
logger.LogError(ex, "Failed to authenticate player {PlayerId}", playerId);
```

### Intercepting Unity Logs

**UnityLogInterceptor** subscribes to `Application.logMessageReceivedThreaded` and forwards every Unity log to the factory under the `"Unity"` category, so `Debug.Log` / `Debug.LogError` output also lands in the log file.

```csharp
var interceptor = new UnityLogInterceptor(loggerFactory);

// Dispose to unsubscribe (e.g. on application shutdown).
interceptor.Dispose();
```

Unity log types map to MEL levels: `Log` → Information, `Warning` → Warning, `Error`/`Exception` → Error, `Assert` → Critical. In the editor the forwarded `"Unity"` category is filtered out of the Unity Debug provider, so intercepted logs reach the file without being echoed back to the console as duplicates.

### Settings

Configure everything from **Edit > Project Settings > Logging**:

| Setting | Default | Description |
|---------|---------|-------------|
| Minimum Level | `Debug` | MEL minimum log level for all providers |
| Log Directory Name | `Logs` | Subdirectory under `Application.persistentDataPath` |
| Max Log Files | `5` | Older files beyond this count are deleted on startup |
| File Prefix | `game` | Log file name prefix; files are `{prefix}_{timestamp}.log` |
| Timestamp Format | `yyyy-MM-dd_HH-mm-ss` | Timestamp embedded in each file name |
| Pretty Stacktrace | `false` | ZLogger pretty stacktrace in the editor Debug provider |

File logging in the editor is off by default. Toggle **Enable File Logging in Editor** in the same panel; it takes effect on the next Play Mode enter.

## Design Decisions

- **Preloaded ScriptableObject for settings** — `LoggingSettings.Instance` resolves before the DI container exists, so the factory builder needs no injected dependencies.
- **Platform-specific outputs via `#if` directives** — the editor gets `AddZLoggerUnityDebug`, a dedicated server (`UNITY_SERVER`) gets `AddZLoggerConsole`, and any build optionally gets `AddZLoggerFile`.
- **`UnityLogInterceptor` uses a `[ThreadStatic]` guard** — prevents re-entrant forwarding when the Unity Debug provider itself emits a log, and the static is reset on Play Mode enter to survive domain-reload-disabled sessions.
