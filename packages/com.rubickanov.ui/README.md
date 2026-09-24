# UI Framework

UI Toolkit framework: views with view models on four layers, popups, dialogs, tooltips and a spinner, with one pointer-capture signal and one back stack for the game's cursor and Escape key.

## Dependencies

> `UniTask` comes from a git URL, not from UPM — UPM will not pull it in for you. See [Third-party dependencies](https://github.com/rubickanov/unity-packages#third-party-dependencies).

- `UniTask` — async register / show / hide and animations
- `R3` — reactive properties, commands and bindings in `ViewModelBase` and `View<TViewModel>`
- `ObservableCollections` — the source of `BindList` (NuGet, like `R3`)

Show/hide animations live in the `com.rubickanov.ui.animations` extension; this package ships only `NoneAnimation`. Unity `6000.0+`.

## Architecture

```text
IUIService ── UIService(root, UxmlLoader)
  ├── screen-layer   Screen views: one active, a history to go back through
  ├── hud-layer      HUD views: independent
  ├── popup-layer    popups (PopupHost)
  └── overlay-layer  Overlay views: independent; modal popups, dialogs, tooltips
        View ── View<TViewModel> ◄── binds ── ViewModelBase

IPopupService ── PopupHost(root, UIService)    code-built panels, placement, close rules
  ├── ShowView<T>()          popup views: a registered view in its own popup
  ├── CreateDialog() ...     confirm / alert / modal / custom dialogs
  └── AttachTooltip()        hover tooltips
SpinnerHost(root)            busy indicator on the overlay layer
UxmlLoader ◄── UxmlLoaders.FromCatalog(UxmlCatalog)
```

`PopupHost` reports its popups to `IUIService`, so `PointerCaptured` and `Back()` cover views and popups alike.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.UI** | Yes | Views, service, popups, dialogs, tooltips, spinner, UXML catalog, default stylesheet |
| **Rubickanov.UI.Editor** | Editor | `Rubickanov/UI Debug` window |

## Core Concepts

**View** — authored UXML plus a view model, one instance per view type, registered once and shown many times. Repeated elements (list rows, markers) are child views, kept in step with an `ObservableList<T>` by `BindList`, or popups.

**Popup** — a transient panel described with a `PopupBuilder`: placement, modal or passive, close rules. Its content may be a registered view with its own view model. Popup views, dialogs and tooltips are popup presets.

**Layer** — declared by the view, not by whoever registers it:

| Layer | Behaviour |
|---|---|
| `Screen` | At most one visible. Showing a screen hides the current one; `Navigate` records it in a history that `Back()` returns through. Intercepts input, captures the pointer, pushes a back handler. |
| `Popup` | A popup view. Registering only loads its UXML; `popups.ShowView<T>(viewModel)` opens a popup with a new instance of it, as many at once as you like. `ui.Show` and `ui.Get` throw for it. |
| `HUD`, `Overlay` | Independent. `Show`/`Hide` only; no pointer capture, no back handler, untouched by `HideScreen`. Do not intercept input. |

**View model lifetime** — the view model passed to `Show` belongs to that show. When the view unbinds it (hide finished, another `Show` replaced it, `Unregister`, service disposed) the service disposes it, together with everything it made through `CreateProperty`, `CreateCommand`, `CreateSubject` and `TrackDisposable`. `Show` with the instance already bound keeps the binding: `OnBind` does not run again and nothing is disposed. Never reuse a view model after its view hid: build a new one per show.

**ViewState** — `Hidden`, `Showing`, `Shown`, `Hiding`; `IsVisible` is `Showing` or `Shown`. A new transition cancels the running one, so show and hide can be called in any order at any time.

## Quick Start

1. The `UIDocument` root holds four elements named `screen-layer`, `hud-layer`, `popup-layer`, `overlay-layer`.
2. Create a catalog (**Create → Rubickanov → UI → UXML Catalog**) and add each view's UXML, named after the view type.
3. Build the service, register views, show them.

```csharp
var root = uiDocument.rootVisualElement;
var ui = new UIService(root, UxmlLoaders.FromCatalog(uxmlCatalog));
var popups = new PopupHost(root, ui);

await ui.Register<HudView>();
await ui.Show<HudView>(new HudViewModel(ship));
```

With VContainer, register `UIService` as itself and as `IUIService`, and `PopupHost` as `IPopupService`; both are `IDisposable`. Dialogs are extension methods on `IPopupService` and need no registration.

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
    protected override UILayer Layer => UILayer.Screen;

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

`BindSlider`, `BindToggle` and `BindDropdown` also have one-way overloads taking an initial value and a callback. A two-way binding ends its own round trip: setting a field to the value it holds sends no `ChangeEvent`, and a `ReactiveProperty` does not emit an equal value. `BindTextField` still skips an equal value, because `TextField` rewrites its text on every set and would drop an IME composition.

### View options

```csharp
public sealed class ScoreboardView : View<ScoreboardViewModel>
{
    protected override UILayer Layer => UILayer.HUD;
    protected override bool InterceptsInput => true;                   // a HUD element with buttons
    protected override IViewAnimation Animation => ViewAnimations.Fade; // from ui.animations
    protected override string? UxmlName => "Scoreboard";               // default: the type name

    protected override bool OnBack() => false; // screens: default goes back in the history; popup views: true keeps the popup open
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

### Lists

```csharp
public sealed class LobbyViewModel : ViewModelBase
{
    public ObservableList<LobbyMember> Members { get; }
    public LobbyViewModel(Lobby lobby) => Members = lobby.Members;
}

public sealed class LobbyView : View<LobbyViewModel>
{
    protected override UILayer Layer => UILayer.Screen;
    protected override IReadOnlyList<Type> ChildViews => new[] { typeof(MemberRowView) };

    protected override void OnBind()
    {
        BindList<LobbyMember, MemberRowView, MemberRowViewModel>(
            ViewModel.Members, Root.Q("members"), member => new MemberRowViewModel(member));
    }
}
```

One child view per item, in the list's order. An added item gets a row with a new view model; a removed or replaced one loses its row, and the row's view model is disposed; a moved, sorted or reversed item keeps its row. `Clear` removes every row. Rows stay together after whatever else the container holds, such as a header or an "empty" label; put anything that goes below them in another element. The source is any `IObservableCollection<T>`: a collection without indices (`ObservableHashSet<T>`) finds rows by equality and appends. Change the source on the main thread. The binding ends when the view unbinds, like every other binding.

### Showing and hiding

```csharp
await ui.Show<PauseView>(new PauseViewModel(audio)); // completes when the show animation ends
ui.Hide<PauseView>();          // instant
await ui.HideAsync<PauseView>(); // animated

ui.HideScreen();               // the active screen, whichever it is; HideScreenAsync() animates
var hud = ui.Get<HudView>();
ui.Unregister<HudView>();      // destroys the view, or cancels a registration still loading
```

`Show` with a view model of the wrong type throws `ArgumentException` before anything changes. The service updates its bookkeeping (active screen, capture, back handlers) at the call, not after the animation.

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

Captures are counted. A screen holds one while showing or shown, `PopupHost` holds one for every modal or interactive popup (buttons, input, a view, close button, hover-close).

### Escape: the back stack

```csharp
if (Keyboard.current.escapeKey.wasPressedThisFrame && !ui.Back())
    await ui.Show<PauseView>(new PauseViewModel(audio));

using var back = ui.PushBackHandler(() => { targeting.Cancel(); return true; });
```

The package does not read the keyboard. `Back()` runs handlers last-pushed first until one returns `true`. The visible screen pushes `OnBack()`; popups with `PopupCloseTriggers.Escape` close on `Back()`, after asking their view's `OnBack()`. The visible screen's handler always runs last, so anything open over a screen answers first even if it opened before the screen was shown.

### Screen history

```csharp
await ui.Navigate<MainMenuView>(() => new MainMenuViewModel(session));
await ui.Navigate<HostView>(() => new HostViewModel(session));
await ui.Navigate<LobbyView>(() => new LobbyViewModel(session.Lobby));

ui.Back();                // lobby → host; again → main menu; then false: the game's own Escape
await ui.NavigateBack();  // the same from a "Back" button; false when there is nowhere to go

// A screen already in the history is returned to: the screens above it are dropped.
await ui.Navigate<MainMenuView>(() => new MainMenuViewModel(session));

// Show of a screen is a jump, not a step: it clears the history.
await ui.Show<ShipScreen>(new ShipScreenViewModel(ship));
```

`Navigate` takes a factory, not a view model: a view model lives for one show, so every return builds a new one. A screen's default `OnBack()` goes back while its screen is the current one of the history; its handler runs after every other one, so a popup over it still closes first. `Show` of a screen, `Hide` of the current screen and `HideScreen` clear the history; `Unregister` drops the screen from it. `CanNavigateBack` says whether there is a screen to return to. `Navigate` to a view on another layer throws.

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
    .At(PopupPlacement.Screen(PopupAnchorCorner.TopRight, new Vector2(16, 16)))   // 16 px in from the corner
    .CloseOn(PopupCloseTriggers.Escape)
    .Open();

handle.SetMessage("Kestrel is waiting.");        // SetTitle, SetIcon; handle.Panel for anything else
var result = await handle.Result;               // ButtonId, Reason, InputText

popups.Create().Message("Autosaved").Timeout(2f).Open(); // passive, clicks pass through
```

A button closes the popup with its id. `Modal()` adds a backdrop and puts the popup on the overlay layer, unless `OnLayer(...)` names another. Close triggers: `Escape` (on `Back()`), `CloseButton`, `ClickOutside` (modal), `PointerLeave`; `Timeout(seconds)` closes it by itself. Disposing the handle closes the popup. `Close()` completes `Result` at once, then plays the hide animation (`.Animation(...)` or the `PopupHost` default) and removes the elements. `CloseAll()` closes every popup; `DismissOthers()` closes them with `PopupCloseReason.Replaced`.

### Popup views

```csharp
public sealed class InventoryView : View<InventoryViewModel>
{
    protected override UILayer Layer => UILayer.Popup;
    protected override IViewAnimation Animation => ViewAnimations.Fade;   // from ui.animations

    protected override void OnBind() =>
        BindButton(Root.Q<Button>("close"), () => Popup!.Close());       // the view reaches its popup

    protected override bool OnBack() => ViewModel.CloseDetails();        // true: Back handled, popup stays
}

await ui.Register<InventoryView>();                                      // loads its UXML
var inventory = popups.ShowView<InventoryView>(new InventoryViewModel(ship));
```

`ShowView` opens a popup on the popup layer with a new instance of the view, stretched over the layer with no panel chrome (`popup--view`), playing the view's own animation. It captures the pointer and closes on `Back()` unless the view's `OnBack()` returns `true`. Several can be open at once, each with its own instance and view model. `Popup` is the view's handle to close it or read `IsOpen`.

### A view as popup content

```csharp
await ui.Register<InviteFriendsView>();   // once, like any view: loads its UXML

var picked = new InviteFriendsViewModel(friends);
var handle = popups.Create()
    .Title("Invite a friend")
    .Content<InviteFriendsView>(picked)
    .CloseOn(PopupCloseTriggers.CloseButton | PopupCloseTriggers.Escape)
    .Modal()
    .Open();
picked.Invited.Subscribe(_ => handle.Close("invited"));

await popups.CreateDialog("Crew").Content<CrewListView>(new CrewListViewModel(ship))
    .Button("Close", "close").OpenAsync();
```

The popup builds a new instance of the registered view from its UXML, below the title and message, binds the view model and owns both: the view is destroyed and the view model disposed when the popup's elements are removed, after the hide animation; if `Open` throws, the view model is disposed at once. The view may be on any layer: a screen's registered instance stays where it is, so one view can be a screen and popup content at the same time. Its child views and lists work as anywhere else, and its UXML stays loaded until the popup closes, even if the view is unregistered meanwhile. A popup with a view is interactive: it captures the pointer.

### Placement

```csharp
PopupPlacement.ScreenCenter();
PopupPlacement.ScreenPoint(panelPoint);
PopupPlacement.Fill();                                       // stretched over the whole layer
PopupPlacement.AtElement(button, PopupSide.Right);           // flips at screen edges
PopupPlacement.Cursor(new Vector2(12, 12));
PopupPlacement.AtWorld(crewMember.Head, worldOffset: Vector3.up * 0.3f,
    screenOffset: new Vector2(0, -8), clampToScreen: false);  // marker may leave the screen
```

World and cursor popups follow every frame. The camera is the placement's own, else the `camera` provider given to `PopupHost`, else `Camera.main` looked up once per frame. A world popup hides while its anchor is behind the camera, and lets go of the pointer, its back handler and its backdrop until the anchor is back in view; it closes with `PopupCloseReason.AnchorDestroyed` once the anchor is destroyed. `SetPlacement` on a closed popup does nothing. Pass `pointerScreenPosition` to `PopupHost` for cursor popups; without it the position only updates over pickable elements.

### Dialogs

```csharp
if (await popups.ShowConfirm("Abandon ship?", "The crew will eject.", "Abandon", "Stay"))
    ship.Abandon();

await popups.ShowAlert("Connection lost", "Host closed the session.");

using (popups.ShowModal("Joining", "Waiting for host..."))
    await session.JoinAsync(address);

var named = await popups.CreateDialog("Rename ship").Input("Name", ship.Name)
    .Button("Save", "save", isPrimary: true).Button("Cancel", "cancel").OpenAsync();
if (named.ButtonId == "save") ship.Rename(named.InputText!);
```

Dialogs are extension methods on `IPopupService`. `CreateDialog` returns a `PopupBuilder` for a modal popup centered on the overlay layer with the `popup--dialog` class, closing on `Back()`; a dialog closed by `Back()` has no `ButtonId`. `ShowConfirm` is `true` only for its confirm button. `ShowModal` cannot be closed but by its handle.

### Tooltips

```csharp
fuelGauge.AttachTooltip(popups, "Reaction mass left");
thrustLabel.AttachTooltip(popups, () => $"Thrust {ship.Thrust:0} kN");
var manipulator = moduleIcon.AttachTooltip(popups, () => BuildModuleCard(module), delay: 0.5f);
moduleIcon.RemoveManipulator(manipulator);

slot.AttachPopup(popups, popup => popup.Content(() => BuildItemCard(item))
    .At(PopupPlacement.AtElement(slot, PopupSide.Right)));
```

A tooltip is a passive popup with the `popup--tooltip` class below the element, closed when the pointer leaves it or the element leaves the panel (a rebuilt list row). It sits on the overlay layer, so it shows over dialogs too. For a 3D object open a popup with `PopupPlacement.Cursor()` or `AtWorld` and close its handle. `AttachPopup` describes a custom hover popup on a fresh `PopupBuilder` each time it opens.

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

Every rule reads a `--ui-*` variable with a fallback. Class names are constants on `PopupStyle` (`popup-panel`, `popup--dialog`, `popup--tooltip`, `popup--view`, ...) and `SpinnerHost` (`spinner`, `spinner__icon`, `spinner__label`).

### Debug window

**Rubickanov → UI Debug** lists, per `UIService` in Play Mode: pointer capture count, back stack depth, the active screen, the screen history, the registered popup views, and the other registered views by layer with their `ViewState`. **Hide Screen** hides the active screen.

## Design Decisions

- **Layer on the view** — a view's layer is a fact about the view; registration sites cannot disagree about it.
- **One popup system** — `PopupHost` alone owns what is open over the screens: placement, close rules, pointer capture and Back. A popup view is not a second stack in `UIService`; it is a registered UXML that `ShowView` puts in a popup of its own, a fresh instance per popup. Binding stays with the view, everything about being open with the popup.
- **A builder, not a public config** — `PopupBuilder` is the one way to describe a popup; presets (dialogs, tooltips, popup views) are extension methods returning or opening one.
- **History of factories, not of view models** — a view model belongs to one show, so the history keeps how to build one. A screen returned to starts fresh; state that must survive a round trip lives in the model the factory reads.
- **Rows are child views** — `BindList` creates and destroys child views; UI Toolkit's `ListView` virtualizes rows for thousands of items, which lobby and crew lists do not have.
- **Catalog, not `Resources`** — `Resources` ships every asset in the folder and finds views by strings that break silently on rename; Addressables adds a build step for a few kilobytes of UXML. The loader stays a delegate, so a project can still use Addressables.
- **No keyboard input in the package** — the game owns its input bindings and calls `Back()`; the package owns the order.
- **The animation belongs to the view** — callers never pass durations; `Animation` decides how a view shows and hides.
