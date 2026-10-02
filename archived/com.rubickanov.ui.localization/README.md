# UI Localization

Binds label and button text and layout direction to localized strings, and creates view-model-owned localized values. Extension for [UI](../com.rubickanov.ui/), bridging it to [Localization](../com.rubickanov.localization/).

## Dependencies

- `com.rubickanov.ui` — base package: `View<TViewModel>.Bind`, `ViewModelBase.TrackDisposable`
- `com.rubickanov.localization` — `ILocalizationService`, `LocalizationKey`, `LocalizedValue`
- `R3` — `OnLocaleChanged` and `IsRTL` subscriptions
- `com.unity.localization` — `Locale`, carried by `OnLocaleChanged`

`ILocalizationService.InitializeAsync` must complete before a binding or value is created; before that `GetString` returns fallbacks.

## Quick Start

```csharp
public sealed class MainMenuView : View<MainMenuViewModel>
{
    protected override UILayer Layer => UILayer.Screen;

    protected override void OnBind()
    {
        var loc = ViewModel.Localization;
        this.BindLocalized(loc, Root.Q<Label>("title"), new LocalizationKey("MainMenu", "Title"));
        this.BindLocalized(loc, Root.Q<Button>("host"), new LocalizationKey("MainMenu", "Host"));
    }
}
```

A binding sets the text at once and again on every locale change. It is cleared with the view's other bindings on unbind. The view gets the service from its view model, like any other data.

## Usage

### Composed and parameterized text

```csharp
this.BindLocalized(loc, sectorLabel,
    l => $"{l.GetString(new LocalizationKey("Hud", "Sector"))} {ViewModel.SectorName}");

this.BindLocalized(loc, crewLabel, new LocalizationKey("Lobby", "CrewCount"),
    () => new object[] { ViewModel.CrewCount.CurrentValue });
```

The factory overloads exist for `Label` and `Button`; the arguments overload (Smart Strings) for `Label`. Factories run again on every locale change.

### Right-to-left layout

```csharp
this.BindIsRTL(loc, Root.Q("toolbar"));
```

Sets `flexDirection` to `RowReverse` for right-to-left locales and `Row` otherwise.

### Localized values in a view model

```csharp
public sealed class MainMenuViewModel : ViewModelBase
{
    public ILocalizationService Localization { get; }
    public LocalizedValue Subtitle { get; }

    public MainMenuViewModel(ILocalizationService loc)
    {
        Localization = loc;
        Subtitle = this.CreateLocalized(loc, new LocalizationKey("MainMenu", "Subtitle"));
    }
}
```

The value is disposed with the view model, when its view hides. An invalid (`default`) `LocalizationKey` throws `ArgumentException` in every overload.
