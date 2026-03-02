# Logging

ZLogger-based logging factory with file rotation, platform-specific outputs, and Unity log interception.

## Dependencies

- `ZLogger.Unity` — structured logging to file and Unity console

## Architecture

```
LoggerFactoryBuilder ──► ILoggerFactory (MEL)
                              │
                    ┌─────────┼─────────┐
                    ▼         ▼         ▼
               ZLoggerFile  Unity    Console
              (all builds)  Debug    (server)
                           (editor)
```

**LoggerFactoryBuilder** creates a Microsoft.Extensions.Logging `ILoggerFactory` configured with ZLogger providers. Output targets depend on the platform: file logging (optional, all builds), Unity Debug log (editor), console (dedicated server).

**LoggingSettings** is a preloaded ScriptableObject with settings for minimum log level, file rotation, and formatting.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Logging.Runtime** | Yes | LoggerFactoryBuilder, LoggingSettings, UnityLogInterceptor |
| **LoggingEditor** | Editor | Project Settings provider, auto-creates settings asset |

## Quick Start

1. Open **Project Settings > Logging** -- the settings asset is created automatically.
2. Build the logger factory at application startup:

```csharp
bool enableFile = !Application.isEditor || EditorPrefs.GetBool("Logging.EnableFileInEditor");
var loggerFactory = LoggerFactoryBuilder.Build(LoggingSettings.Instance, enableFile);
```

3. Register in your LifetimeScope:

```csharp
builder.RegisterInstance(loggerFactory).As<ILoggerFactory>();
```

## Usage

### Creating Loggers

```csharp
var logger = loggerFactory.CreateLogger<NetworkSession>();
logger.LogInformation("Connected to {Host}:{Port}", host, port);
logger.LogWarning("Packet loss: {Loss}%", packetLoss);
logger.LogError(ex, "Failed to authenticate");
```

### Intercepting Unity Logs

**UnityLogInterceptor** bridges `Application.logMessageReceivedThreaded` to MEL, so Unity's `Debug.Log` / `Debug.LogError` output also reaches the file logger.

```csharp
var interceptor = new UnityLogInterceptor(loggerFactory);

// Dispose to unsubscribe
interceptor.Dispose();
```

### Log File Configuration

Settings are configured via **Project Settings > Logging**:

| Setting | Default | Description |
|---------|---------|-------------|
| Minimum Level | `Debug` | MEL minimum log level |
| Log Directory Name | `Logs` | Subdirectory in `persistentDataPath` |
| Max Log Files | `5` | Old logs beyond this count are deleted |
| File Prefix | `game` | Log file name prefix |
| Timestamp Format | `yyyy-MM-dd_HH-mm-ss` | Timestamp in file names |
| Pretty Stacktrace | `false` | ZLogger pretty stacktrace in editor |

### Editor File Logging

File logging in the editor is off by default. Toggle it via **Project Settings > Logging > Enable File Logging in Editor**. Takes effect on next Play Mode enter.

## Design Decisions

- **Preloaded ScriptableObject for settings** — available before DI container is built. No constructor injection needed for the factory builder.
- **Platform-specific outputs via #if directives** — editor gets ZLoggerUnityDebug, dedicated server gets ZLoggerConsole, all builds optionally get ZLoggerFile.
- **UnityLogInterceptor uses ThreadStatic guard** — prevents infinite recursion when ZLogger's Unity provider calls `Debug.Log` internally.
