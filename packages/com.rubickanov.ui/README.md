# UI Framework

UI Toolkit framework: views with view models on four layers, a screen history, popups, unscaled-time animations, and one pointer capture, back stack and menu navigation for the game's cursor, Escape key and pad.

## Dependencies

> `UniTask` comes from a git URL, not from UPM — UPM will not pull it in for you. See [Third-party dependencies](https://github.com/rubickanov/unity-packages#third-party-dependencies).

- `UniTask` — async register / show / hide and animations
- `R3` — reactive properties, commands and bindings in `ViewModelBase` and `View<TViewModel>`, the menu's focus and cues (NuGet)

Unity `6000.0+`. The package reads no input device: the game reads its own actions and feeds them in.

## Architecture

```text
IUIService ── UIService(root, UxmlLoader)
  ├── screen-layer   Screen views: one active, a history Back returns through
  ├── hud-layer      HUD views: independent
  ├── popup-layer    popups (PopupHost)
  └── overlay-layer  Overlay views: independent; modal popups
        View ── View<TViewModel> ◄── binds ── ViewModelBase

IPopupService ── PopupHost(root, IUIService)   content, placement, close rules
MenuNavigation ── MenuModel                    claim stack for keyboard and pad menus
CursorLock(IUIService)                         the hardware cursor from PointerCaptured
UxmlLoader ◄── UxmlLoaders.FromCatalog(UxmlCatalog)
```

Every view that takes input, every interactive popup and every input scope holds a pointer capture and a back handler on `IUIService`, so `PointerCaptured` and `Back()` cover all of them.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.UI** | Yes | Views, service, popups, animations, navigation, UXML catalog |
| **Rubickanov.UI.Editor** | Editor | `Rubickanov/UI Debug` window |

## Core Concepts

**View** — authored UXML (or a tree built in code) plus a view model, one instance per view type, registered once and shown many times. How a game lays out lists inside a view is its own business.

**Layer** — declared by the view, not by whoever registers it:

| Layer | Behaviour |
|---|---|
| `Screen` | At most one visible. Showing a screen hides the current one; `Navigate` records it in a history that `Back()` returns through. Takes input. |
| `Popup` | A popup view. Registering only loads its UXML; `popups.ShowView<TView, TViewModel>(viewModel)` opens a popup with a new instance of it, as many at once as you like. `ui.Show` and `ui.Get` throw for it. |
| `HUD`, `Overlay` | Independent. `Show`/`Hide` only. Take input only when `InterceptsInput` says so. |

**Taking input** — a visible view with `InterceptsInput` (screens by default) blocks pointer events, holds a pointer capture and answers `Back()` with its `OnBack()`.

**View model lifetime** — the view model passed to `Show` belongs to that show. When the view unbinds it (hide finished, another `Show` replaced it, `Unregister`, service disposed) the service disposes it, together with everything it made through `CreateProperty`, `CreateCommand`, `CreateSubject` and `TrackDisposable`. Build a new one per show.

**ViewState** — `Hidden`, `Showing`, `Shown`, `Hiding`; `IsVisible` is `Showing` or `Shown`. A new transition cancels the running one, so show and hide can be called in any order at any time.

## Quick Start

1. The `UIDocument` root holds four elements named `screen-layer`, `hud-layer`, `popup-layer`, `overlay-layer`.
2. Create a catalog (**Create → Rubickanov → UI → UXML Catalog**) and add each view's UXML, named after the view type.
3. Build the services, register views, show them.

```csharp
var root = uiDocument.rootVisualElement;
var ui = new UIService(root, UxmlLoaders.FromCatalog(uxmlCatalog));
var popups = new PopupHost(root, ui);

await ui.Register<HudView>();
await ui.Show<HudView, HudViewModel>(new HudViewModel(ship));
```

With VContainer, register `UIService` as `IUIService`, `PopupHost` as `IPopupService` (it takes `IUIService`), and `MenuNavigation` and `CursorLock` as singletons; all are `IDisposable`.

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

`OnBind` is synchronous: load icons or await requests before `Show`. Everything bound in `OnBind` is cleared on unbind; `OnUnbind()` and `TrackUnbind(action)` cover the rest. `OnInitialize()` runs once, when the tree is built: a code-only view (`UxmlName => null`) builds its tree there.

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

`BindSlider`, `BindToggle` and `BindDropdown` also have one-way overloads taking an initial value and a callback. A two-way binding ends its own round trip; `BindTextField` skips an equal value, so an IME composition survives.

### View options

```csharp
public sealed class HintsView : View<HintsViewModel>
{
    private VisualElement _line = null!;

    protected override UILayer Layer => UILayer.HUD;
    protected override IViewAnimation Animation => ViewAnimations.Fade;
    protected override VisualElement AnimationTarget => _line;  // the root covers the screen; the line moves
    protected override bool InterceptsInput => false;           // true: blocks clicks, frees the pointer, answers Back
    protected override string? UxmlName => "Hints";             // default: the type name

    protected override void OnInitialize() => _line = Root.Q("line");
    protected override void OnBind() { }
}
```

### Showing and hiding

```csharp
await ui.Show<PauseView, PauseViewModel>(new PauseViewModel(audio)); // completes when the show animation ends
ui.Hide<PauseView>();             // instant
await ui.HideAsync<PauseView>();  // animated
var hud = ui.Get<HudView>();
ui.Unregister<HudView>();         // destroys the view, or cancels a registration still loading
```

A view model of the wrong type does not compile: `TView` must be a `View<TViewModel>`. The service updates its bookkeeping (active screen, captures, back handlers) at the call, not after the animation. Shown again while hiding, a view keeps its place in the animation (see Animations).

### Screen history

```csharp
await ui.Navigate<MainMenuView, MainMenuViewModel>(() => new MainMenuViewModel(session));
await ui.Navigate<LobbyView, LobbyViewModel>(() => new LobbyViewModel(session.Lobby));

ui.Back();   // lobby → main menu; then false: the game's own Escape

protected override bool OnBack()   // in EpisodesView
{
    var model = ViewModel;
    if (model.CloseDetails()) return true;
    var back = base.OnBack();      // goes back once this returns: ViewModel is still bound here
    if (back) ViewModel.CueBack();
    return back;
}
```

`Navigate` takes a factory: a view model lives for one show, so every return builds a new one. A screen already in the history is returned to and the screens above it are dropped. `Show` of a screen, `Hide` of the current screen and `Unregister` change the history; `CanNavigateBack` tells whether there is a screen to return to. A Back button on a screen calls `ui.Back()`, the same path as Escape.

### Escape and pause: the back stack

```csharp
// Each frame, from the game's own actions: Escape is back and pause, a pad's B only back, its Start only pause.
ui.BackOrPause(input.Cancel.WasPressedThisFrame(), input.Pause.WasPressedThisFrame(), OpenPauseMenu);

using var back = ui.PushBackHandler(() => { targeting.Cancel(); return true; });
```

`Back()` runs handlers last-pushed first until one returns `true`. Views that take input push `OnBack()`; the visible screen's handler always runs last, so anything open over a screen answers first. A modal popup stops `Back()` whether or not it closes on it.

### Cursor: pointer capture

```csharp
var cursor = new CursorLock(ui);       // the one writer of Cursor.lockState and Cursor.visible
using (cursor.Lock())                  // the camera looks with the mouse: locked while nothing captures
    await RunCourseAsync();

using (ui.CapturePointer())            // free the cursor without opening UI: a console, a debug toggle
    await PickTargetAsync();
```

Captures are counted. The cursor is locked while at least one `Lock()` is held and `PointerCaptured` is false, and written again when the window gets its focus back.

### Input scope

```csharp
// A panel of the game's own, not a view: Back, the pointer and the menu keys in one handle.
_scope = ui.OpenInputScope(OnBack, navigation, settingsMenu);
_scope.Dispose();
```

Without a back handler Back stops at the scope unanswered, as at a modal popup.

### Menus: keyboard and pad

```csharp
// Each frame, from the game's own actions; while its menu input is off: navigation.WaitForRelease().
navigation.Update(new MenuInput
{
    Navigate = input.Navigate.ReadValue<Vector2>(),
    Page = input.Page.ReadValue<float>(),
    Tab = input.Tab.WasPressedThisFrame() ? Math.Sign(input.Tab.ReadValue<float>()) : 0,
    Submit = input.Submit.WasPressedThisFrame(),
}, Time.unscaledTime);

Menu = new MenuModel(new[]
{
    new MenuEntry("RESUME", Resume),
    new MenuEntry("RESTART", Restart, enabled: canRestart),
    new MenuEntry("STAY", Close).WithCue(MenuCue.Back),
}, navigation);
TrackDisposable(Menu);

navigation.Cues.Subscribe(cue => audio.Play(SoundOf(cue)));
```

Only the last claim hears the keys: a `MenuModel` claims while it lives, `navigation.Claim(target)` claims for any `IMenuTarget`. A direction steps once on a press and again while held (after 0.4 s, every 0.1 s, on the time passed in); one already held when a menu claims waits for its release. `MenuModel` focuses over the entries that are on, without wrapping; `Point(i)` and `Click(i)` take the mouse; `Focus` and `Pressed` are for the view. Cues `Focus`, `Select`, `Back` and `Denied` come from the package; a game adds its own as `new MenuCue("tape")`. For a wheel, `WheelSteps.Take(evt.delta.y, out var direction)` steps once per notch however many events a touchpad sends.

### Animations

```csharp
public sealed class Squeeze : IViewAnimation
{
    public UniTask PlayShowAsync(VisualElement target, CancellationToken ct) =>
        Tween.Run(0.25f, t => target.style.scale = new Scale(new Vector2(1f, BackOut(t))), ct);

    public UniTask PlayHideAsync(VisualElement target, CancellationToken ct) =>
        Tween.Run(0.2f, t => target.style.scale = new Scale(new Vector2(1f, 1f - t * t)), ct);

    public void Reset(VisualElement target) => target.style.scale = StyleKeyword.Null;
}
```

`Tween.Run(seconds, apply, ct)` calls `apply` once a frame with 0..1 on unscaled time and last with 1, so a pause menu at `Time.timeScale` 0 still opens and closes. `ViewAnimations.Fade` (`new FadeAnimation(seconds)` for another length) fades from wherever the opacity is: a view shown again mid-hide fades back in instead of flickering from zero. A show may start mid-hide and a hide mid-show; an animation should start from where the target is.

### Popups

```csharp
var toast = popups.Create()
    .Content(() => new Label("Autosaved"))
    .At(PopupPlacement.Screen(PopupAnchorCorner.TopRight, new Vector2(16, 16)))
    .PassThrough()                                  // clicks go through, no pointer capture
    .Open();
toast.Close();

var result = await popups.Create()
    .Content<InviteFriendsView, InviteFriendsViewModel>(new InviteFriendsViewModel(friends))
    .Modal()
    .CloseOn(PopupCloseTriggers.Escape | PopupCloseTriggers.ClickOutside)
    .OpenAsync();                                   // result.Id, result.Reason
```

A popup is a frame over its layer holding a panel with one content: a built element or a new instance of a registered view (any layer), bound to its view model. Without `PassThrough()` the panel takes clicks and captures the pointer. `Modal()` makes the frame a backdrop that blocks input below and stops `Back()`, and puts the popup on the overlay layer unless `OnLayer(...)` names another. Close triggers: `Escape` (on `Back()`, after asking the content view's `OnBack()`), `ClickOutside` (modal). `Close(id)` completes `Result` at once, then plays the hide animation (`.Animation(...)`, else the content view's own on its `AnimationTarget`, else the `PopupHost` default) and removes the elements, destroying the content view and disposing its view model. A builder with a view model opens once. `CloseAll()` closes every popup; `DismissOthers()` closes them with `PopupCloseReason.Replaced`. `Class`, `Style` and the `PopupStyle` class names are for the game's stylesheet: the package sets layout only.

### Popup views

```csharp
public sealed class ConfirmView : View<ConfirmViewModel>
{
    protected override UILayer Layer => UILayer.Popup;

    protected override void OnBind() =>
        Bind(ViewModel.Answered, answer => Popup!.Close(answer));        // the view reaches its popup

    protected override bool OnBack() => ViewModel.CloseDetails();        // true: Back handled, popup stays
}

await ui.Register<ConfirmView>();
var answer = await popups.ShowView<ConfirmView, ConfirmViewModel>(new ConfirmViewModel("Quit?")).Result;
if (answer.Id == "yes") Quit();
```

`ShowView` opens a popup on the popup layer stretched over it (`popup--view`), with a new instance of the view playing its own animation. It captures the pointer and closes on `Back()` unless the view's `OnBack()` returns `true`.

### Placement

```csharp
PopupPlacement.ScreenCenter(new Vector2(0, -40));           // the default
PopupPlacement.Screen(PopupAnchorCorner.BottomLeft, new Vector2(24, 24));
PopupPlacement.Fill();                                       // stretched over the whole layer
```

Placement is flex alignment of the panel in its frame, not a measured position: a panel that grows stays in place.

### Scene-scoped registration

```csharp
var scope = new ScopedViewRegistration(ui);   // one per scene entry point
await scope.Register<HangarView>();
await scope.Register<LoadoutView>();
scope.Dispose();                              // unregisters them; a view still loading is cancelled
```

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

### Debug window

**Rubickanov → UI Debug** lists, per `UIService` in Play Mode: pointer capture count, back stack depth, the active screen, the screen history, the registered popup views, and the other registered views by layer with their `ViewState`.

## Design Decisions

- **Layer on the view** — a view's layer is a fact about the view; registration sites cannot disagree about it.
- **One popup system** — `PopupHost` alone owns what is open over the screens: placement, close rules, pointer capture and Back. A popup view is a registered UXML that `ShowView` puts in a popup of its own, a fresh instance per popup.
- **No chrome, no presets** — popups carry no title, buttons, dialogs or tooltips and the package ships no stylesheet: what a question or a spinner looks like is the game's, and the games built their own on `ShowView`. The package keeps the mechanics: layers, input, close rules, ownership.
- **History of factories, not of view models** — a view model belongs to one show, so the history keeps how to build one.
- **No device input in the package** — the game owns its bindings and feeds `Back()`, `BackOrPause` and `MenuNavigation.Update`; the package owns the order: who hears a key, what Back reaches.
- **Unscaled time for animations** — a UI animation that runs on scaled time never finishes on a pause menu.
- **Catalog, not `Resources`** — `Resources` ships every asset in the folder and finds views by strings that break silently on rename; the loader stays a delegate, so a project can still use Addressables.
