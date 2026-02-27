# com.rubickanov.logging

ZLogger-based logging factory with file rotation, platform-specific outputs, and Unity log interception.

## Key Types

| Type | Description |
|------|-------------|
| `LoggerFactoryBuilder` | Builds `ILoggerFactory` with ZLogger file + platform outputs |
| `LoggingSettings` | ScriptableObject settings (preloaded asset, accessible via Project Settings) |
| `UnityLogInterceptor` | Bridges `Application.logMessageReceived` to MEL `ILogger` |

## Usage

```csharp
// Build logger factory (settings loaded automatically)
var loggerFactory = LoggerFactoryBuilder.Build(LoggingSettings.Instance, enableFileLogging: true);

// Intercept Unity logs
var interceptor = new UnityLogInterceptor(loggerFactory);

// Create typed loggers
var logger = loggerFactory.CreateLogger<MyService>();
logger.LogInformation("Service started");
```

## Editor

`LoggingSettingsProvider` adds a **Project Settings > Logging** panel to configure logging settings and toggle file logging in Editor.
