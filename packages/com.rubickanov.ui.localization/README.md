# UI Localization

Localization binding helpers that bridge the [UI](../com.rubickanov.ui/) framework with the [Localization](../com.rubickanov.localization/) service. Provides one-line reactive binding of `Label`/`Button`/`VisualElement` to localized strings and locale direction, plus a ViewModel helper for disposable `LocalizedValue` creation.

## Dependencies

- `com.rubickanov.ui` — `ViewModelBase`, `UIToolkitView<TVM>`, `BindObservable`
- `com.rubickanov.localization` — `ILocalizationService`, `LocalizationKey`, `LocalizedValue`
- `R3` — reactive observable source (already required by UI)
- `UniTask` — async/await

## Quick Start

Inside a `UIToolkitView<TVM>`, bind labels and buttons to localization keys:

```csharp
public class MainMenuView : UIToolkitView<MainMenuViewModel>
{
    protected override void OnInitialize()
    {
        var title = Root.Q<Label>("title");
        var playButton = Root.Q<Button>("play");

        this.BindLocalized(title, LocKeys.MainMenu_Title);
        this.BindLocalized(playButton, LocKeys.MainMenu_Play);
    }
}
```

Bindings re-evaluate on every `OnLocaleChanged` emission and are disposed automatically when the view is unbound.

## Usage

### Direct key binding

```csharp
this.BindLocalized(label, LocKeys.Dialog_Confirm);
this.BindLocalized(button, LocKeys.Dialog_Cancel);
```

### Factory binding

Use a factory when the text depends on more than one key or needs composition:

```csharp
this.BindLocalized(subtitleLabel, loc => $"{loc.GetString(LocKeys.Level_Prefix)} {levelIndex}");
```

### Parameterized (Smart Strings) binding

For strings with format arguments that need to re-evaluate on locale change:

```csharp
this.BindLocalized(
    scoreLabel,
    LocKeys.Hud_Score,
    argsFactory: () => new object[] { _vm.Score.CurrentValue });
```

### Layout direction (RTL)

Toggle `flexDirection` between `Row` and `RowReverse` based on the current locale's direction:

```csharp
this.BindIsRTL(rootContainer);
```

### Explicit service (no DI)

Every binding has an overload that accepts `ILocalizationService` directly — useful in tests or when the service is not registered in the UI container:

```csharp
this.BindLocalized(loc, label, LocKeys.MainMenu_Title);
this.BindIsRTL(loc, rootContainer);
```

### ViewModel-side reactive values

Inside a `ViewModelBase`, create a `LocalizedValue` whose subscription is tracked against the ViewModel's lifetime:

```csharp
public sealed class MainMenuViewModel : ViewModelBase
{
    public LocalizedValue Title { get; }

    public MainMenuViewModel(ILocalizationService loc)
    {
        Title = this.CreateLocalized(loc, LocKeys.MainMenu_Title);
    }
}
```

The value is disposed automatically when the ViewModel is unbound.

## Notes

- `ILocalizationService.InitializeAsync` must complete before any binding is created — otherwise `GetString` returns fallbacks.
- `LocalizationKey` must be valid (both `Table` and `Key` non-empty). Bindings throw `ArgumentException` on `default(LocalizationKey)`.
- Text assignments go through an equality check — repeated same-string assignments do not touch the `VisualElement`.
