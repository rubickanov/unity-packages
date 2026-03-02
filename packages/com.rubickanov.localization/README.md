# Localization

Localization service wrapping Unity Localization with reactive locale tracking, `LocalizedString` caching, and strongly-typed key generation.

## Dependencies

- `R3` — reactive properties and observables
- `UniTask` — async initialization and locale switching
- `Unity.Localization` — underlying localization backend
- `ZLogger` — structured logging
- `com.rubickanov.storage` — locale persistence via `IStorageService`

## Architecture

```
ILocalizationService
├── LocalizationService      — Unity Localization + R3 reactive + caching
└── NullLocalizationService  — no-op for server/headless builds
```

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Localization.Runtime** | Yes | Service interface, implementation, data types |
| **Localization.Editor** | Editor | Key generator, auto-regeneration postprocessor, Project Settings panel |

## Core Concepts

**LocalizationKey** — readonly struct combining a table name and entry key (e.g., `"UI"`, `"game_title"`). Used by all lookup methods.

**LocalizedValue** — reactive wrapper that auto-updates its string value when the locale changes. Exposes `Value` as a `ReadOnlyReactiveProperty<string>`.

**LangLocale** — locale descriptor with code, English name, and native name (e.g., `"ru"`, `"Russian"`, `"Русский"`).

## Quick Start

1. Set up Unity Localization with String Tables.
2. Register in your LifetimeScope:

```csharp
builder.Register<IStorageService, PlayerPrefsStorageService>(Lifetime.Singleton);
builder.Register<ILocalizationService, LocalizationService>(Lifetime.Singleton);
```

3. Initialize on startup:

```csharp
await localizationService.InitializeAsync();
```

## Usage

### One-Shot Read

```csharp
string text = localization.GetString(L.Ui.StartButton);

// With format arguments
string msg = localization.GetString(L.Ui.WelcomeMessage, playerName);
```

### Reactive Binding

```csharp
LocalizedValue title = localization.Localize(L.Ui.GameTitle);

// Subscribe to updates (auto-fires on locale change)
title.Value.Subscribe(text => label.text = text);

// Read current value directly
string current = title.CurrentValue;

// Change key dynamically
title.SetKey(L.Ui.PauseTitle);

// Dispose when done
title.Dispose();
```

### Changing Locale

```csharp
await localization.SetLocaleAsync("ru");

// Or with a LangLocale struct
LangLocale locale = availableLocales[selectedIndex];
await localization.SetLocaleAsync(locale);
```

### Available Locales

```csharp
LangLocale[] locales = localization.GetAvailableLocales();
// locales[0].Code       → "en"
// locales[0].Name       → "English"
// locales[0].NativeName → "English"
```

### Reactive Locale Tracking

```csharp
// Current locale as reactive property
localization.CurrentLocale.Subscribe(locale =>
    Debug.Log($"Locale: {locale.NativeName}"));

// RTL detection
localization.IsRTL.Subscribe(isRtl =>
    SetLayoutDirection(isRtl));
```

### Key Generation

The editor generates a static class with strongly-typed keys from Unity String Table Collections.

```csharp
// Auto-generated from String Tables
L.Ui.GameTitle        // → LocalizationKey("UI", "game_title")
L.Items.Sword         // → LocalizationKey("Items", "sword")
L.Dialogs.NpcGreeting // → LocalizationKey("Dialogs", "npc_greeting")
```

Configure via **Project Settings > Localization Generator** or run manually via **Tools > Generators > Localization**. Auto-regeneration triggers when String Table assets are modified.

## Design Decisions

- **Persistence via IStorageService** — if an `IStorageService` is injected, the selected locale is persisted and restored automatically. Without it, locale resets each session.
- **LocalizedString caching** — `GetOrCreateLocalizedString()` caches Unity `LocalizedString` instances by key, avoiding repeated allocations.
- **LocalizedValue.SetKey() reuses cache** — uses a resolver delegate from the service, so switching keys on a reactive value still benefits from the cache.
