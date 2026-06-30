# UI Framework

Backend-agnostic UI framework with view lifecycle, layer management, dialogs, tooltips, and flexible popups. Ships with UI Toolkit and UGUI backends.

## Dependencies

- `UniTask` — async view lifecycle (register / show / hide, animations)
- `R3` — reactive properties and commands in `ViewModelBase`, two-way bindings in the UI Toolkit backend
- `UnityEngine.UI`, `Unity.TextMeshPro` — referenced by the UGUI backend only

Ready-made show/hide animations live in the `com.rubickanov.ui.animations` extension package. This package only ships `NoneAnimation` (instant).

Unity `6000.0+`.

## Architecture

```
IUIService (view registry + show/hide by layer)
├── UIService          — backend-agnostic implementation
└── NullUIService      — no-op for server/headless builds

IViewFactory (view creation + layer attachment)
├── UIToolkitViewFactory — UI Toolkit backend
└── UGUIViewFactory      — UGUI backend

IView (view contract, no Root)
├── UIToolkitViewBase → UIToolkitView<TViewModel>
└── UGUIViewBase      → UGUIView<TViewModel>

IDialogService (confirm / alert / modal)
└── UIToolkitDialogService — built on popups / DialogBuilder

IPopupService (flexible panels, placed anywhere, modal or passive)
└── PopupHost

ISpinnerHost (busy indicator)
├── UIToolkitSpinnerHost
└── NullSpinnerHost

TooltipService          (hover tooltips on the overlay layer)
IViewServiceResolver    (optional service lookup from inside views)
SceneViewScopeService   (scene-lifetime view registration)
```

`UIService` keeps a registry of views, one active **screen** (a view registered on `UILayer.Screen`) and a stack of **popups** (views registered on any other layer). The render layer is chosen once at `Register<T>` time, not per show. The factory owns all DOM operations; views never place themselves.

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **Rubickanov.UI.Runtime** | No | Core abstractions: `IView`, `IUIService`, `IDialogService`, `UIService`, `ViewModelBase`, animation interfaces |
| **Rubickanov.UI.UIToolkit** | Yes | UI Toolkit backend: views, factory, dialogs, tooltips, flexible popups, spinner host |
| **Rubickanov.UI.UGUI** | Yes | UGUI backend: views and factory |
| **Rubickanov.UI.Editor** | Editor | `UIServiceDebugWindow` for inspecting live `UIService` state |

## Core Concepts

**IView** — Backend-agnostic view contract: `Bind`, `Show`/`Hide`, `ShowAsync`/`HideAsync`, `Destroy`, `IsVisible`. No `Root` at this level.

**UILayer** — Render order: `Screen`, `HUD`, `Popup`, `Overlay`. Each maps to a container element (`screen-layer`, `hud-layer`, `popup-layer`, `overlay-layer`) that must exist in the `UIDocument` root. A view registered on `Screen` is treated as the single active screen; any other layer makes it a stacked popup.

**ViewModelBase** — Disposable base for view models. `CreateProperty<T>()`, `CreateCommand()`, `CreateCommand<T>()`, `CreateSubject<T>()` allocate R3 state that is auto-disposed with the view model. `AddDisposable` / `TrackDisposable` track extra disposables.

**UIToolkitView\<TViewModel\>** — Generic UI Toolkit view base with typed `ViewModel` access and binding helpers. It tracks two cleanup mechanisms: an R3 `DisposableBag` for observable subscriptions and an unbind list for UI Toolkit event handlers. Both clear automatically on hide.

## Quick Start

1. The `UIDocument` root must contain four layer elements: `screen-layer`, `hud-layer`, `popup-layer`, `overlay-layer`.

2. Register services (VContainer shown):

```csharp
// Client
builder.Register<IViewFactory>(_ =>
    new UIToolkitViewFactory(uiDocument, LoadUxml, serviceResolver), Lifetime.Singleton);
builder.Register<UIService>(Lifetime.Singleton).As<IUIService>();
builder.Register<IPopupService>(_ => new PopupHost(uiDocument), Lifetime.Singleton);
builder.Register<IDialogService, UIToolkitDialogService>(Lifetime.Singleton);

// Server / headless
builder.Register<NullUIService>(Lifetime.Singleton).As<IUIService>();
builder.Register<NullDialogService>(Lifetime.Singleton).As<IDialogService>();

// UxmlLoader: maps a view's type name to a VisualTreeAsset + a release handle
static async UniTask<(VisualTreeAsset, IDisposable)> LoadUxml(string address)
{
    var handle = Addressables.LoadAssetAsync<VisualTreeAsset>(address);
    return (await handle, new AddressableHandle(handle));
}
```

3. Register a view (its layer is fixed here), then show it:

```csharp
await ui.Register<HudView>(UILayer.HUD);
await ui.Show<HudView>(new HudViewModel(health, ammo));
```

`UIToolkitViewFactory` loads UXML by the view's type name (`HudView` → address `"HudView"`). Override `UxmlName` is not needed; the factory uses `GetType().Name`.

## Usage

### View lifecycle

```text
new() → Root set → OnInitialize() → [ OnBind() → OnShowAsync() → OnHideAsync() → OnViewHide() → OnUnbind() ]* → Destroy()
                   ^ once            ^ repeats per show/hide cycle
```

- `OnInitialize()` — once, after `Root` is assigned (during `Register`). Cache element references here.
- `OnBind()` — each time the view is shown with a new view model. Set up bindings.
- `OnShowAsync(target, duration)` — after display is set to Flex. Play show animation.
- `OnHideAsync(target, duration)` — before display is set to None. Play hide animation.
- `OnViewHide()` — after the hide animation completes.
- `OnUnbind()` — after all bindings are cleared. Final cleanup.

### Creating a view

```csharp
public class HudView : UIToolkitView<HudViewModel>
{
    protected override UniTask OnBind()
    {
        Bind(ViewModel.Health, h => Root.Q<Label>("hp").text = $"{h}");
        BindButton(Root.Q<Button>("reload-btn"), () => ViewModel.Reload.Execute(Unit.Default));
        return UniTask.CompletedTask;
    }
}
```

`Bind`, `BindButton` and the other helpers are auto-cleaned when the view hides — no manual unsubscription. `protected override UniTask OnBind()` is the only required override.

### Registering views

```csharp
// Global (lives forever)
await ui.Register<LoadingScreen>(UILayer.Screen);

// Scoped (auto-unregister on dispose)
var views = new ScopedViewRegistration(ui);
await views.Register<HudView>(UILayer.HUD);
await views.Register<PausePopup>(UILayer.Popup);
// views.Dispose() unregisters all of them
```

### Showing and hiding

A view registered on `UILayer.Screen` is the single active screen — showing another screen hides the previous one. Views on any other layer stack as popups.

```csharp
await ui.Show<HudView>(new HudViewModel(health, ammo));   // screen or popup, per registration

ui.Hide<HudView>();                  // instant
await ui.HideAsync<HudView>();       // animated

ui.HideTop();                        // instant, topmost popup
await ui.HideTopAsync();             // animated, topmost popup

ui.HideAll();                        // instant, screen + all popups
await ui.HideAllAsync();             // animated

var hud = ui.Get<HudView>();         // typed lookup of a registered view
```

### Binding helpers

| Helper | Description | Cleanup |
|--------|-------------|---------|
| `Bind<T>(Observable<T>, Action<T>)` | One-way: view model → UI | DisposableBag |
| `BindButton(Button, Action)` | Click handler | unbind list |
| `BindValueChanged<TElement, TValue>(element, handler)` | Value-change handler | unbind list |
| `BindTextField(TextField, ReactiveProperty<string>)` | Two-way | DisposableBag + unbind list |
| `BindSlider(Slider, ReactiveProperty<float>)` | Two-way | DisposableBag + unbind list |
| `BindToggle(Toggle, ReactiveProperty<bool>)` | Two-way | DisposableBag + unbind list |
| `BindDropdown(DropdownField, ReactiveProperty<int>, List<string>)` | Two-way | DisposableBag + unbind list |
| `TrackUnbind(Action)` | Register a manual cleanup action | unbind list |

`BindSlider`, `BindToggle` and `BindDropdown` also have one-way overloads that take an initial value and an `Action<T>` callback instead of a `ReactiveProperty`.

### Creating a view model

```csharp
public class PauseViewModel : ViewModelBase
{
    public ReactiveCommand Resume { get; }
    public ReactiveCommand Settings { get; }
    public ReactiveCommand Quit { get; }

    public PauseViewModel(Action onResume, Action onSettings, Action onQuit)
    {
        Resume = CreateCommand(onResume);
        Settings = CreateCommand(onSettings);
        Quit = CreateCommand(onQuit);
    }
}
```

| Helper | Returns | Use case |
|--------|---------|----------|
| `CreateProperty<T>(initial)` | `ReactiveProperty<T>` | Observable state with a current value |
| `CreateCommand(action?)` | `ReactiveCommand` | UI action (button click) |
| `CreateCommand<T>(action?)` | `ReactiveCommand<T>` | UI action with a payload |
| `CreateSubject<T>()` | `Subject<T>` | One-shot event, no stored value |
| `AddDisposable(d)` / `TrackDisposable(d)` | — | Track extra disposables for cleanup |

### Dialogs

Standard confirm / alert / modal helpers on `IDialogService`:

```csharp
bool ok = await dialogs.ShowConfirm("Exit", "Are you sure?", "Quit", "Cancel");
await dialogs.ShowAlert("Error", message);
using var modal = dialogs.ShowModal("Loading", "Please wait...");   // closes on Dispose
```

Custom dialogs through `DialogBuilder` (UI Toolkit backend):

```csharp
var dialogs = (UIToolkitDialogService)dialogService;

DialogResult result = await dialogs.CreateDialog("Rename")
    .WithMessage("Enter a new name:")
    .WithInput(placeholder: "name", defaultValue: currentName)
    .AddButton("Cancel", "cancel")
    .AddButton("Save", "save", isPrimary: true)
    .ShowAsync();

if (result.ButtonId == "save")
    rename(result.InputText);
```

Builder options: `WithMessage`, `WithImage(Texture2D)`, `WithContent(Func<VisualElement>)`, `WithInput`, `AddButton(text, id, isPrimary)`. `DialogResult` exposes `ButtonId` and `InputText`. Pressing **Esc** completes with the last button (or empty if none were added). USS class hooks are exposed as static fields on `DialogStyle` (`Panel`, `Title`, `Message`, `Button`, `ButtonPrimary`, …) — reassign them before the first dialog to repoint the engine at your own CSS.

### Flexible popups

`IPopupService` shows configurable panels **anywhere on screen** — modal or passive, opened by code or hover, with any combination of close rules. Many can be open at once. `IDialogService` and tooltips are presets over this same engine.

Register `PopupHost` once. Its `UIDocument` root needs the standard `screen-layer` / `hud-layer` / `popup-layer` / `overlay-layer` elements.

```csharp
// Minimal
new PopupHost(uiDocument);

// With a default stylesheet applied to every popup
new PopupHost(uiDocument, popupStyleSheet);

// With cursor-follow support (see note)
new PopupHost(uiDocument,
    pointerScreenPosition: () => Pointer.current.position.ReadValue());
```

> Cursor- and world-following popups need a live pointer provider. UI Toolkit runtime panels only dispatch `PointerMoveEvent` while a pickable element is under the cursor, so the event-based fallback freezes over empty areas. Pass `pointerScreenPosition` (screen pixels, bottom-left origin) from whatever input backend you use.

Open a popup with the fluent builder:

```csharp
// Centered modal, dismissable by button / X / click-outside / Escape
PopupResult result = await popups.Create()
    .Title("Delete save?")
    .Message("This cannot be undone.")
    .Modal()
    .CloseOn(PopupCloseTriggers.CloseButton | PopupCloseTriggers.ClickOutside | PopupCloseTriggers.Escape)
    .Button("Cancel", "cancel")
    .Button("Delete", "delete", isPrimary: true)
    .OpenAsync();

if (result.ButtonId == "delete") { /* ... */ }
```

```csharp
// Top-right toast that auto-closes after 3s (Open() is fire-and-forget)
popups.Create()
    .Title("Saved")
    .At(PopupPlacement.Screen(PopupAnchorCorner.TopRight, new Vector2(16, 16)))
    .Timeout(3f)
    .Open();
```

Placement modes (`PopupPlacement`):

| Factory | Anchors to |
|---------|-----------|
| `ScreenCenter(offset?)` / `Screen(corner, offset?)` | a region of the screen |
| `ScreenPoint(panelPoint, offset?)` | an explicit panel-space point |
| `AtElement(element, side?, autoFlip?, offset?)` | a UI element, flipping near screen edges |
| `AtWorld(transform, camera?, offset?)` | a world-space object, followed each frame |
| `Cursor(offset?)` | the mouse cursor |

Hover popups — richer than tooltips — via `AttachPopup` (mirrors `AddTooltip`):

```csharp
// Convenience: passive title/message anchored to the element, closes on pointer-leave
element.AttachPopup(popups, "Apple", "A crisp red fruit.");

// Full control through a config factory
element.AttachPopup(popups, () => new PopupConfig
{
    Title = "Inventory slot",
    ContentFactory = BuildSlotDetails,
    Placement = PopupPlacement.AtElement(element, PopupSide.Right),
    CloseTriggers = PopupCloseTriggers.PointerLeave
});
```

`Open()` returns an `IPopupHandle` to drive a live popup:

```csharp
IPopupHandle handle = popups.Create().Title("Loading").At(PopupPlacement.ScreenCenter()).Open();
handle.UpdateContent(c => c.SetMessage("Almost there…"));
handle.SetPlacement(PopupPlacement.Cursor());
handle.Close();                       // or: await handle.Result
```

Restyle via theme variables in `Popup.uss` (`--popup-*`), by defining the `.popup-*` classes in your own theme, or per-popup with `.Style(sheet)` / `.Class("popup--danger")`. Class hooks and modifiers (`popup--modal`, `popup--side-{top,bottom,left,right}`, …) are exposed as static fields on `PopupStyle`.

### Tooltips

`TooltipService` shows hover tooltips on the overlay layer for any `VisualElement`, and at an arbitrary screen position for 3D objects.

```csharp
var tooltips = new TooltipService(uiDocument);                 // optional StyleSheet 2nd arg

// Elements — via extension method (returns the manipulator for removal)
var m = element.AddTooltip(tooltips, "Reload weapon");
element.AddTooltip(tooltips, "Slow tooltip", delay: 0.5f);
element.AddTooltip(tooltips, () => BuildRichTooltip());         // rich content factory
element.RemoveTooltip(m);

// 3D objects — drive by screen position (e.g. from a raycast)
tooltips.Show(Input.mousePosition, "Treasure chest");
tooltips.UpdatePosition(Input.mousePosition);
tooltips.Hide();
```

Style with the `.tooltip-container` and `.tooltip-text` USS classes in your theme.

### Busy spinner

`ISpinnerHost` shows a corner busy indicator. `Show` returns an `IDisposable`; the spinner is visible while at least one handle is alive, and the most recent label wins.

```csharp
using (spinner.Show("Saving…"))
{
    await SaveGame();
}   // spinner hides when the handle is disposed
```

Register `UIToolkitSpinnerHost` on the client (needs the `overlay-layer`) and `NullSpinnerHost` for headless builds.

### Service resolution from views

Views resolve services through `IViewServiceResolver`, an adapter over your DI container:

```csharp
public class VContainerServiceResolver : IViewServiceResolver
{
    private readonly IObjectResolver _container;
    public VContainerServiceResolver(IObjectResolver container) => _container = container;
    public T? Resolve<T>() where T : class => _container.Resolve<T>();
}
```

Pass it to `UIToolkitViewFactory`, then inside a view:

```csharp
protected override UniTask OnBind()
{
    var audio = GetService<IAudioService>();   // throws if not registered
    audio.Play("hover");
    return UniTask.CompletedTask;
}
```

`GetService<T>` calls `IViewServiceResolver.Require<T>()` and throws if the service is missing. Call `Resolve<T>()` directly when a null return is acceptable.

### Scene-scoped registration

`SceneViewScopeService` registers views that auto-unregister when the scene scope ends:

```csharp
public class GameplayScene : IDisposable
{
    private readonly ScopedViewRegistration _views;

    public GameplayScene(SceneViewScopeService scope)
    {
        _views = scope.Begin();   // disposes the previous scope, if any
    }

    public async UniTask Load()
    {
        await _views.Register<HudView>(UILayer.HUD);
        await _views.Register<PausePopup>(UILayer.Popup);
    }

    public void Dispose() => _views.Dispose();   // unregisters both views
}
```

Calling `Begin()` again disposes the previous scope — one active scope per service.

### Animations

Views show/hide instantly by default (`NoneAnimation`). Override `OnShowAsync` / `OnHideAsync` to add transitions. Both receive an `IAnimationTarget` exposing `Opacity`, `TranslateX/Y`, `ScaleX/Y`, `SetVisible`, `ResetAnimationState`.

```csharp
public class PausePopup : UIToolkitView<PauseViewModel>
{
    protected override async UniTask OnShowAsync(IAnimationTarget root, float duration)
    {
        root.Opacity = 0f;
        // tween root.Opacity → 1 over `duration` with your tweening library
    }
}
```

For per-element animation, wrap a sub-element in a `UIToolkitAnimationTarget`:

```csharp
private UIToolkitAnimationTarget _panel = default!;

protected override void OnInitialize()
    => _panel = new UIToolkitAnimationTarget(Root.Q(className: "panel"));
```

Reusable animations implement `IViewAnimation` (`PlayShowAsync` / `PlayHideAsync`). The `com.rubickanov.ui.animations` extension provides a ready-made library (fade, scale, slide) built on LitMotion.

### Cursor visibility

`UIService.SetVisibilityCallback(Action<bool>)` fires `true` when UI becomes visible and `false` once everything is hidden. Wire it to a cursor service in your DI setup:

```csharp
builder.RegisterBuildCallback(resolver =>
{
    var ui = (UIService)resolver.Resolve<IUIService>();
    var cursor = resolver.Resolve<ICursorService>();
    ui.SetVisibilityCallback(visible => cursor.SetVisible(visible));
});
```

## Design Decisions

- **IView has no Root** — keeps the contract backend-agnostic. `UIToolkitViewBase` adds `VisualElement Root`; the UGUI base adds its own.
- **Layer fixed at registration** — `Register<T>(layer)` decides screen-vs-popup and the container once, so `Show<T>` carries no placement argument.
- **IViewFactory owns all DOM operations** — creation, UXML loading, layer attachment. Views never place themselves.
- **UIService is backend-agnostic** — it delegates to `IViewFactory` and reports visibility through an `Action<bool>` callback instead of depending on a cursor service.
- **UxmlLoader delegate instead of an asset service** — `UIToolkitViewFactory` takes a `UxmlLoader` delegate, avoiding a hard dependency on any asset-loading strategy (Addressables, Resources, …).
- **Dialogs, tooltips, spinner are presets over the popup engine** — one placement/lifecycle system serves modals, hovers, toasts and busy indicators.

## File Structure

```
com.rubickanov.ui/
├── Runtime/
│   ├── IView.cs / IUIService.cs / IViewFactory.cs
│   ├── IDialogService.cs / ISpinnerHost.cs / IViewServiceResolver.cs
│   ├── IViewAnimation.cs / IAnimationTarget.cs / NoneAnimation.cs
│   ├── UIService.cs / ViewModelBase.cs / UILayer.cs
│   ├── ScopedViewRegistration.cs / SceneViewScopeService.cs
│   └── NullUIService.cs / NullDialogService.cs / NullSpinnerHost.cs
├── UIToolkit/
│   ├── UIToolkitViewBase.cs / UIToolkitView.cs / UIToolkitViewFactory.cs
│   ├── UIToolkitAnimationTarget.cs / UIToolkitSpinnerHost.cs
│   ├── UIToolkitDialogService.cs / DialogBuilder.cs / DialogResult.cs / DialogStyle.cs
│   ├── DynamicPopup.cs / DynamicDialogViewModel.cs
│   ├── Tooltip/   (TooltipService, TooltipManipulator, TooltipExtensions, Tooltip.uss)
│   └── Popup/     (PopupHost, PopupBuilder, PopupConfig, PopupPlacement, IPopupHandle, …)
├── UGUI/
│   └── UGUIViewBase.cs / UGUIView.cs / UGUIViewFactory.cs / UGUIAnimationTarget.cs
└── Editor/
    └── UIServiceDebugWindow.cs
```
