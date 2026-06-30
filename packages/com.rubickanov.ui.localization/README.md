# UI Localization

Localization binding helpers that bridge the [UI](../com.rubickanov.ui/) framework with the [Localization](../com.rubickanov.localization/) service. Extension for [UI](../com.rubickanov.ui/).

One-line reactive binding of `Label` / `Button` text and layout direction to localized strings, plus a ViewModel helper for lifetime-tracked `LocalizedValue` creation.

## Dependencies

- `com.rubickanov.ui` — `ViewModelBase`, `UIToolkitView<TVM>`, `BindObservable`, `GetService`, `TrackDisposable`
- `com.rubickanov.localization` — `ILocalizationService`, `LocalizationKey`, `LocalizedValue`
- `R3` — `Observable` / `ReadOnlyReactiveProperty` subscriptions (`OnLocaleChanged`, `IsRTL`); auto-referenced
- `com.unity.localization` — `Locale` type carried by `OnLocaleChanged`

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.UI.Localization.Runtime** | No | Backend-agnostic ViewModel helper (`CreateLocalized`) |
| **Rubickanov.UI.Localization.UIToolkit** | Yes | UIToolkit binding extensions for `Label` / `Button` / `VisualElement` |

## Quick Start

Bindings are created inside `OnBind` so they are registered against the view's
bind lifecycle and disposed automatically when the view is unbound. Resolve the
service via `GetService<ILocalizationService>()` (provided by the UI container).

```csharp
public sealed class MainMenuView : UIToolkitView<MainMenuViewModel>
{
    protected override UniTask OnBind()
    {
        var title = Root.Q<Label>("title");
        var play = Root.Q<Button>("play");

        this.BindLocalized(title, new LocalizationKey("MainMenu", "Title"));
        this.BindLocalized(play, new LocalizationKey("MainMenu", "Play"));

        return UniTask.CompletedTask;
    }
}
```

Each binding sets the current text immediately and re-evaluates on every
`OnLocaleChanged` emission. `ILocalizationService.InitializeAsync` must have
completed before a binding is created, otherwise `GetString` returns fallbacks.

## Usage

### Direct key binding

`Label` and `Button` overloads bind `.text` to a `LocalizationKey`.

```csharp
this.BindLocalized(label, new LocalizationKey("Dialog", "Confirm"));
this.BindLocalized(button, new LocalizationKey("Dialog", "Cancel"));
```

### Factory binding

Use a factory when the text composes more than one key or mixes in dynamic data.
The factory receives the service and re-runs on locale change.

```csharp
this.BindLocalized(
    subtitle,
    loc => $"{loc.GetString(new LocalizationKey("Level", "Prefix"))} {levelIndex}");
```

### Parameterized (Smart Strings) binding

The `Label` parameterized overload re-evaluates `argsFactory` on every locale
change and formats via `GetString(key, args)`.

```csharp
this.BindLocalized(
    scoreLabel,
    new LocalizationKey("Hud", "Score"),
    () => new object[] { ViewModel.Score.CurrentValue });
```

### Layout direction (RTL)

`BindIsRTL` toggles an element's `flexDirection` between `Row` (LTR) and
`RowReverse` (RTL) reactively from `ILocalizationService.IsRTL`.

```csharp
this.BindIsRTL(rootContainer);
```

### Explicit service (no DI)

Every binding has an overload that takes `ILocalizationService` directly —
useful in tests or presenters where the service is not resolved from the UI
container. The service is the first argument after the view.

```csharp
this.BindLocalized(loc, label, new LocalizationKey("MainMenu", "Title"));
this.BindIsRTL(loc, rootContainer);
```

### ViewModel-side reactive values

`CreateLocalized` (in the Runtime assembly, no UIToolkit dependency) builds a
`LocalizedValue` via `ILocalizationService.Localize` and tracks its disposal
against the ViewModel. The value unsubscribes when the ViewModel is unbound.

```csharp
public sealed class MainMenuViewModel : ViewModelBase
{
    public LocalizedValue Title { get; }

    public MainMenuViewModel(ILocalizationService loc)
    {
        Title = this.CreateLocalized(loc, new LocalizationKey("MainMenu", "Title"));
    }
}
```

## Notes

- `LocalizationKey` must be valid (non-empty `Table` and `Key`). Bindings throw
  `ArgumentException` on `default(LocalizationKey)`.
- Text assignments go through an equality check — re-assigning the same string
  does not touch the `VisualElement`.
