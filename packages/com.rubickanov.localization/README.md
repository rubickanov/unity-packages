# Localization

Localization service wrapping Unity Localization with reactive locale tracking, `LocalizedString` caching, optional locale persistence, and strongly-typed key generation.

## Dependencies

- `com.rubickanov.storage` — optional locale persistence via `IStorageService` (constructor parameter is nullable; locale is only remembered between sessions when a storage service is provided)
- `Unity.Localization` — underlying String Tables and locale backend
- `Unity.ResourceManager` — async initialization handle used by `InitializeAsync`
- `UniTask` — async initialization and locale switching
- `R3` — reactive properties and observables
- `ZLogger` + `Microsoft.Extensions.Logging` — `ILoggerFactory` is a required constructor dependency

Unity 6000.0+.

## Architecture

```
ILocalizationService
├── LocalizationService      — Unity Localization + R3 reactive + caching + persistence
└── NullLocalizationService  — no-op for server/headless builds
```

`LocalizationService` subscribes to `LocalizationSettings.SelectedLocaleChanged`, mirrors the active locale into reactive properties, and persists the choice through `IStorageService` when one is supplied. `NullLocalizationService` returns empty strings and completed tasks so game code that depends on `ILocalizationService` runs unchanged on dedicated servers.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.Localization.Runtime** | Yes | Service interface, implementations, key and locale types |
| **Rubickanov.Localization.Editor** | Editor | Key generator, auto-regeneration postprocessor, Project Settings panel |

## Core Concepts

**LocalizationKey** — readonly struct pairing a String Table name with an entry key (e.g. `("UI", "start_button")`). All lookups take one. `IsValid` is false for `default(LocalizationKey)`, and looking up an invalid key throws.

**LocalizedValue** — reactive wrapper around a single localized string. `Value` is a `ReadOnlyReactiveProperty<string>` that re-emits on every locale change; the key and format arguments can be swapped at runtime.

**LangLocale** — readonly struct describing a locale by `Code`, English `Name`, and `NativeName`. Built from a code alone, it resolves names from a built-in table (28 languages) and falls back to `CultureInfo`.

## Quick Start

1. Set up Unity Localization with one or more String Table Collections.
2. Register the service. `ILoggerFactory` is required; `IStorageService` is optional (enables persistence):

```csharp
builder.Register<ILoggerFactory, ZLoggerLoggerFactory>(Lifetime.Singleton);
builder.Register<IStorageService, PlayerPrefsStorageService>(Lifetime.Singleton);
builder.Register<ILocalizationService, LocalizationService>(Lifetime.Singleton);
```

3. Initialize once on startup — completes after Unity Localization is ready and the saved locale (if any) is restored:

```csharp
await localization.InitializeAsync();
```

## Usage

### One-Shot Read

```csharp
string text = localization.GetString(L.Ui.StartButton);

// With format arguments (string.Format applied to the resolved entry)
string msg = localization.GetString(L.Ui.WelcomeMessage, playerName);
```

### Reactive Binding

```csharp
LocalizedValue title = localization.Localize(L.Ui.GameTitle);

// Subscribe — fires immediately and on every locale change
title.Value.Subscribe(text => titleLabel.text = text);

// Read the current value without subscribing
string current = title.CurrentValue;

// Swap the key — reuses the service's LocalizedString cache
title.SetKey(L.Ui.PauseTitle);

// Swap format arguments for Smart String / string.Format entries
LocalizedValue greeting = localization.Localize(L.Ui.Greeting, playerName);
greeting.SetArguments(otherPlayerName);

// Dispose when the view goes away
title.Dispose();
greeting.Dispose();
```

### Changing Locale

```csharp
// By code — completes only after Unity has applied the locale
await localization.SetLocaleAsync("ru");

// Or by LangLocale
LangLocale locale = localization.GetAvailableLocales()[selectedIndex];
await localization.SetLocaleAsync(locale);
```

Setting a locale that is already active, or a code with no matching locale, returns immediately without switching.

### Available Locales

```csharp
LangLocale[] locales = localization.GetAvailableLocales();
// locales[0].Code        → "en"
// locales[0].Name        → "English"
// locales[0].NativeName  → "English"
```

`GetAvailableLocales` is cached during `InitializeAsync` and returns an empty array before then.

### Reactive Locale Tracking

```csharp
// Active locale as a reactive property
localization.CurrentLocale.Subscribe(locale =>
    Debug.Log($"Locale: {locale.NativeName}"));

// Right-to-left detection (ar, he, fa, ur, ...)
localization.IsRTL.Subscribe(SetLayoutDirection);

// Raw Unity Locale events, if you need the underlying type
localization.OnLocaleChanged.Subscribe(unityLocale =>
    Debug.Log(unityLocale.Identifier.Code));
```

### Key Generation

The editor generates a static class of strongly-typed keys from your String Table Collections. Keys containing dots are split into nested classes, giving hierarchical autocomplete; keys without dots stay flat.

```csharp
// Generated for a table named "App":
L.App.Ui.Menu.Play              // → new LocalizationKey("App", "ui.menu.play")
L.App.Ui.Settings.MasterVolume  // → new LocalizationKey("App", "ui.settings.master_volume")
L.App.Fruit.Apple               // → new LocalizationKey("App", "fruit.apple")

// Flat key in a table named "Items":
L.Items.Sword                   // → new LocalizationKey("Items", "sword")
```

Configure output path, namespace, class name, and auto-regeneration under **Project Settings > Localization Generator**. Generate manually via **Tools > Generators > Localization**, or let the asset postprocessor regenerate automatically when String Table assets change.

## Design Decisions

- **Persistence is opt-in** — `IStorageService` is a nullable constructor parameter. With it, the selected locale is saved under `localization.locale` and restored on init; without it, the locale resets each session. Saves are chained so concurrent locale changes never race.
- **LocalizedString caching** — `Localize` and `GetString` share one `LocalizedString` instance per `LocalizationKey`, so repeated lookups and `LocalizedValue.SetKey` reuse allocations rather than recreating Unity objects.
- **NullLocalizationService for headless builds** — server code keeps the same `ILocalizationService` dependency without pulling in Unity Localization initialization.
- **LangLocale equality normalizes empty codes** — `default(LangLocale)` and `LangLocale.Empty` compare equal and hash equally, so empty locales behave consistently in sets and dictionaries.
