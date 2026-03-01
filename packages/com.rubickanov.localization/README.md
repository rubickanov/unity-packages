# Localization

Localization service with reactive locale tracking, `LocalizedString` caching, and strongly-typed key generation. Wraps Unity Localization.

## Architecture

```
ILocalizationService
├── LocalizationService      — Unity Localization impl with R3 reactive + caching
└── NullLocalizationService  — no-op for server/headless builds
```

## Key Types

| Type | Description |
|------|-------------|
| `ILocalizationService` | Interface for locale management with reactive updates |
| `LocalizationService` | Implementation backed by Unity Localization, caches `LocalizedString` instances |
| `NullLocalizationService` | No-op implementation for server builds |
| `LocalizedValue` | Reactive wrapper that auto-updates on locale change |
| `LocalizationKey` | Strongly-typed `(Table, Key)` struct for type-safe access |
| `LangLocale` | Locale descriptor with code, English name, and native name |

## Assemblies

- **Localization.Runtime** — service interface, implementation, data types. Depends on R3, UniTask, Unity.Localization.
- **Localization.Editor** — code generator and auto-regeneration postprocessor. Editor-only.

## Usage

### Registration

```csharp
// In LifetimeScope — wire persistence delegates to your storage backend
builder.Register<LocalizationService>(Lifetime.Singleton)
    .WithParameter<Func<string?>>(
        () => storage.GetString("locale"))
    .WithParameter<Action<string>>(
        code => storage.SetString("locale", code).Forget())
    .As<ILocalizationService>();
```

### Reactive binding

```csharp
// Create a reactive localized value — auto-updates on locale change
LocalizedValue title = localization.Localize(L.Ui.GameTitle);
title.Value.Subscribe(text => label.text = text);

// One-shot read
string text = localization.GetString(L.Ui.StartButton);

// Change locale
await localization.SetLocaleAsync("ru");
```

### Key generation

Generated class provides strongly-typed keys from String Tables:

```csharp
// Auto-generated from String Tables
L.Ui.GameTitle        // → LocalizationKey("UI", "game_title")
L.Items.Sword         // → LocalizationKey("Items", "sword")
```

Configure via **Project Settings / Localization Generator** or run manually via **Tools / Generators / Localization**.
