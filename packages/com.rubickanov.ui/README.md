# UI Framework

UI Toolkit framework: views with view models on four layers, popups, dialogs, tooltips and a spinner, with one pointer-capture signal and one back stack for the game's cursor and Escape key.

## Dependencies

> `UniTask` comes from a git URL, not from UPM — UPM will not pull it in for you. See [Third-party dependencies](https://github.com/rubickanov/unity-packages#third-party-dependencies).

- `UniTask` — async register / show / hide and animations
- `R3` — reactive properties, commands and bindings in `ViewModelBase` and `View<TViewModel>`

Show/hide animations live in the `com.rubickanov.ui.animations` extension; this package ships only `NoneAnimation`. Unity `6000.0+`.

## Architecture

```text
IUIService ── UIService(root, UxmlLoader)
  ├── screen-layer   Screen views: one active
  ├── hud-layer      HUD views: independent
  ├── popup-layer    Popup views: a stack
  └── overlay-layer  Overlay views: independent
        View ── View<TViewModel> ◄── binds ── ViewModelBase

IPopupService ── PopupHost(root, IUIService)   code-built panels, placement, close rules
  ├── DialogService          confirm / alert / modal / custom dialogs
  └── AttachTooltip()        hover tooltips
SpinnerHost(root)            busy indicator on the overlay layer
UxmlLoader ◄── UxmlLoaders.FromCatalog(UxmlCatalog)
```

`PopupHost` reports its popups to `IUIService`, so `PointerCaptured` and `Back()` cover views and popups alike.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.UI** | Yes | Views, service, popups, dialogs, tooltips, spinner, UXML catalog, default stylesheet |
| **Rubickanov.UI.Editor** | Editor | `Tools/Rubickanov/UI Debug` window |

## Core Concepts

**View** — authored UXML plus a view model, one instance per view type, registered once and shown many times. Repeated elements (list rows, markers) are child views or popups.

**Popup** — a transient panel built in code from a `PopupConfig`: placement, modal or passive, close rules. Dialogs and tooltips are popup presets.

**Layer** — declared by the view, not by whoever registers it:

| Layer | Behaviour |
|---|---|
| `Screen` | At most one visible. Showing a screen hides the current one. Intercepts input, captures the pointer, pushes a back handler. |
| `Popup` | A stack. `Show` pushes (a popup already visible is rebound and moves to the top), `HideTop` pops. Intercepts input, captures the pointer, pushes a back handler. |
| `HUD`, `Overlay` | Independent. `Show`/`Hide` only; not on any stack, no pointer capture, no back handler, untouched by `HideTop` and `HideAll`. Do not intercept input. |

**View model lifetime** — the view model passed to `Show` belongs to that show. When the view unbinds it (hide finished, another `Show` replaced it, `Unregister`, service disposed) the service disposes it, together with everything it made through `CreateProperty`, `CreateCommand`, `CreateSubject` and `TrackDisposable`. `Show` with the instance already bound rebinds and disposes nothing. Never reuse a view model after its view hid: build a new one per show.

**ViewState** — `Hidden`, `Showing`, `Shown`, `Hiding`; `IsVisible` is `Showing` or `Shown`. A new transition cancels the running one, so show and hide can be called in any order at any time.

## Quick Start

1. The `UIDocument` root holds four elements named `screen-layer`, `hud-layer`, `popup-layer`, `overlay-layer`.
2. Create a catalog (**Create → Rubickanov → UI → UXML Catalog**) and add each view's UXML, named after the view type.
3. Build the service, register views, show them.

```csharp
var root = uiDocument.rootVisualElement;
var ui = new UIService(root, UxmlLoaders.FromCatalog(uxmlCatalog));
var popups = new PopupHost(root, ui);
var dialogs = new DialogService(popups);

await ui.Register<HudView>();
await ui.Show<HudView>(new HudViewModel(ship));
```

With VContainer, register `UIService` as `IUIService` and `PopupHost` as `IPopupService` in the same way; both are `IDisposable`.

## Usage

### Defining a view and its view model

```csharp
public sealed class PauseViewModel : ViewModelBase
{
    public ReactiveCommand Resume { get; }
    public ReactiveProperty<float> MusicVolume { get; }

    public PauseViewModel(AudioSettings audio)
    {
        Resume = CreateCommand();
        MusicVolume = CreateProperty(audio.MusicVolume);
    }
}

public sealed class PauseView : View<PauseViewModel>
{
    protected override UILayer Layer => UILayer.Popup;

    protected override void OnBind()
    {
        BindButton(Root.Q<Button>("resume"), () => ViewModel.Resume.Execute(Unit.Default));
        BindSlider(Root.Q<Slider>("music"), ViewModel.MusicVolume);
    }
}
```

`OnBind` is synchronous: load icons or await requests before `Show`, in whoever builds the view model. Everything bound in `OnBind` is cleared on unbind; `OnUnbind()` and `TrackUnbind(action)` cover the rest.

### Binding helpers

```csharp
BindText(Root.Q<Label>("speed"), ViewModel.Speed, v => $"{v:0} m/s");
BindVisible(Root.Q("low-fuel"), ViewModel.LowFuel);
BindClass(Root.Q("g-meter"), "g-meter--danger", ViewModel.OverG);
Bind(ViewModel.Hull, hull => _hullBar.value = hull);

BindTextField(Root.Q<TextField>("ship-name"), ViewModel.ShipName);   // two-way
BindToggle(Root.Q<Toggle>("invert-y"), ViewModel.InvertY);           // two-way
BindDropdown(Root.Q<DropdownField>("quality"), ViewModel.Quality, qualityNames);
BindValueChanged<Slider, float>(Root.Q<Slider>("fov"), fov => ViewModel.SetFov(fov));
```

`BindSlider`, `BindToggle` and `BindDropdown` also have one-way overloads taking an initial value and a callback.

### View options

```csharp
public sealed class ScoreboardView : View<ScoreboardViewModel>
{
    protected override UILayer Layer => UILayer.HUD;
    protected override bool InterceptsInput => true;                   // a HUD element with buttons
    protected override IViewAnimation Animation => ViewAnimations.Fade; // from ui.animations
    protected override string? UxmlName => "Scoreboard";               // default: the type name

    protected override bool OnBack() => false; // screens and popups only; popup default: hide and return true
    protected override void OnBind() { }
}
```

A code-only view returns `null` from `UxmlName` and builds its tree on the empty `Root` in `OnInitialize()`, which runs once at registration.

### Child views

```csharp
public sealed class CrewListView : View<CrewListViewModel>
{
    protected override UILayer Layer => UILayer.Screen;
    protected override IReadOnlyList<Type> ChildViews => new[] { typeof(CrewRowView) };

    protected override void OnBind()
    {
        var list = Root.Q("rows");
        foreach (var member in ViewModel.Crew)
            CreateChild<CrewRowView, CrewRowViewModel>(new CrewRowViewModel(member), list);
    }
}
```

The UXML of every type in `ChildViews` loads when the parent registers, so `CreateChild` is synchronous. Children are destroyed, and their view models disposed, when the parent unbinds; `DestroyChildren()` does it earlier. Creating a type not listed throws.

### Showing and hiding

```csharp
await ui.Show<PauseView>(new PauseViewModel(audio)); // completes when the show animation ends
ui.Hide<PauseView>();          // instant
await ui.HideAsync<PauseView>(); // animated

ui.HideTop();                  // top popup view; HideTopAsync() animates
ui.HideAll();                  // the screen and every popup view; HideAllAsync() animates
var hud = ui.Get<HudView>();
ui.Unregister<HudView>();      // destroys the view, or cancels a registration still loading
```

`Show` with a view model of the wrong type throws `ArgumentException` before anything changes. The service updates its bookkeeping (active screen, popup stack, capture, back handlers) at the call, not after the animation.

### Cursor: pointer capture

```csharp
ui.PointerCaptured.Subscribe(captured =>
{
    Cursor.lockState = captured ? CursorLockMode.None : CursorLockMode.Locked;
    Cursor.visible = captured;
});

using (ui.CapturePointer())   // free the cursor without opening UI
    await PickTargetAsync();
```

Captures are counted. Screen and popup views hold one while showing or shown, `PopupHost` holds one for every modal or interactive popup (buttons, input, close button, hover-close).

### Escape: the back stack

```csharp
if (Keyboard.current.escapeKey.wasPressedThisFrame && !ui.Back())
    await ui.Show<PauseView>(new PauseViewModel(audio));

using var back = ui.PushBackHandler(() => { targeting.Cancel(); return true; });
```

The package does not read the keyboard. `Back()` runs handlers last-pushed first until one returns `true`. Visible screen and popup views push `OnBack()`; popups with `PopupCloseTriggers.Escape` close on `Back()`.

### Scene-scoped registration

```csharp
var scope = sceneScopes.Begin();   // SceneViewScopeService: disposes the previous scope
await scope.Register<HangarView>();
await scope.Register<LoadoutView>();
// scene unload: sceneScopes.Begin() for the next scene, or scope.Dispose()
```

`ScopedViewRegistration` unregisters its views on dispose; disposing it while a view still loads cancels that registration.

### UXML catalog

```csharp
[Test]
public void UxmlCatalog_AllGameViews_HaveUxml()
{
    var catalog = AssetDatabase.LoadAssetAtPath<UxmlCatalog>("Assets/UI/UxmlCatalog.asset");
    var viewTypes = TypeCache.GetTypesDerivedFrom<View>().Where(t => !t.IsAbstract && t.Assembly.GetName().Name == "Game");

    CollectionAssert.IsEmpty(catalog.FindMissing(viewTypes));
}
```

The loader looks up assets by name, throws naming the catalog when one is missing, and throws on two assets with the same name. Any other source (Addressables) plugs in as a `UxmlLoader` delegate returning the asset and a release handle.

### Popups

```csharp
var handle = popups.Create()
    .Title("Docking request")
    .Message("Freighter Kestrel asks to dock at bay 2.")
    .Button("Accept", "accept", isPrimary: true)
    .Button("Deny", "deny")
    .At(PopupPlacement.Screen(PopupAnchorCorner.TopRight, new Vector2(-16, 16)))
    .CloseOn(PopupCloseTriggers.ActionButton | PopupCloseTriggers.Escape)
    .Open();

var result = await handle.Result;               // ButtonId, Reason, InputText
handle.UpdateContent(c => c.SetMessage("Kestrel is waiting."));

popups.Create().Message("Autosaved").Timeout(2f).Open(); // passive, clicks pass through
```

`Modal()` adds a backdrop and moves the popup to the overlay layer. Other triggers: `CloseButton`, `ClickOutside` (modal), `PointerLeave`, `Timeout`. `Close()` completes `Result` at once, then plays the hide animation (`.Animation(...)` or the `PopupHost` default) and removes the elements. `CloseAll()` closes every popup.

### Placement

```csharp
PopupPlacement.ScreenCenter();
PopupPlacement.ScreenPoint(panelPoint);
PopupPlacement.AtElement(button, PopupSide.Right);           // flips at screen edges
PopupPlacement.Cursor(new Vector2(12, 12));
PopupPlacement.AtWorld(crewMember.Head, worldOffset: Vector3.up * 0.3f,
    screenOffset: new Vector2(0, -8), clampToScreen: false);  // marker may leave the screen
```

World and cursor popups follow every frame. The camera is the placement's own, else the `camera` provider given to `PopupHost`, else `Camera.main` looked up once per frame. A world popup hides while its anchor is behind the camera. Pass `pointerScreenPosition` to `PopupHost` for cursor popups; without it the position only updates over pickable elements.

### Dialogs

```csharp
if (await dialogs.ShowConfirm("Abandon ship?", "The crew will eject.", "Abandon", "Stay"))
    ship.Abandon();

await dialogs.ShowAlert("Connection lost", "Host closed the session.");

using (dialogs.ShowModal("Joining", "Waiting for host..."))
    await session.JoinAsync(address);

var named = await dialogs.CreateDialog("Rename ship").WithInput("Name", ship.Name)
    .AddButton("Save", "save", isPrimary: true).AddButton("Cancel", "cancel").ShowAsync();
```

Dialogs close on `Back()`; a dialog closed without a button reports its last button's id.

### Tooltips

```csharp
fuelGauge.AttachTooltip(popups, "Reaction mass left");
thrustLabel.AttachTooltip(popups, () => $"Thrust {ship.Thrust:0} kN");
var manipulator = moduleIcon.AttachTooltip(popups, () => BuildModuleCard(module), delay: 0.5f);
moduleIcon.RemovePopup(manipulator);
```

A tooltip is a passive popup with the `popup--tooltip` class below the element, closed when the pointer leaves it. For a 3D object open a popup with `PopupPlacement.Cursor()` or `AtWorld` and close its handle. `AttachPopup` takes a full `Func<PopupConfig>` for custom hover popups.

### Spinner

```csharp
var spinner = new SpinnerHost(root);
using (spinner.Show("Loading sector..."))
    await sector.LoadAsync();
```

Handles are counted; the latest label shows.

### Styles

```css
@import url("/Packages/com.rubickanov.ui/Runtime/Styles/Default.uss");

:root {
    --ui-popup-bg: rgb(12, 18, 28);
    --ui-button-primary-bg: rgb(230, 120, 30);
    --ui-tooltip-font-size: 12px;
}
```

Every rule reads a `--ui-*` variable with a fallback. Class names are constants on `PopupStyle` (`popup-panel`, `popup--dialog`, `popup--tooltip`, ...) and `SpinnerHost` (`spinner`, `spinner__icon`, `spinner__label`).

### Debug window

**Tools → Rubickanov → UI Debug** lists, per `UIService` in Play Mode: pointer capture count, back stack depth, the active screen, the popup stack, and registered views by layer with their `ViewState`.

## Design Decisions

- **Layer on the view** — a view's layer is a fact about the view; registration sites cannot disagree about it.
- **Views and popups stay separate** — views are authored UXML bound to a view model; popups are transient panels with placement and close rules. A UXML view as popup content is not supported.
- **Catalog, not `Resources`** — `Resources` ships every asset in the folder and finds views by strings that break silently on rename; Addressables adds a build step for a few kilobytes of UXML. The loader stays a delegate, so a project can still use Addressables.
- **No keyboard input in the package** — the game owns its input bindings and calls `Back()`; the package owns the order.
- **The animation belongs to the view** — callers never pass durations; `Animation` decides how a view shows and hides.
