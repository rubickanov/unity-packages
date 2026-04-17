# UI Framework

Backend-agnostic UI framework with view lifecycle, layer management, and dialog system. Ships with UI Toolkit and UGUI backends.

## Dependencies

- `UniTask` — async view lifecycle (show/hide/bind)
- `R3` — reactive bindings in UIToolkit/UGUI backends and ViewModelBase

## Architecture

```
IUIService (screen/popup lifecycle)
├── UIService          — backend-agnostic implementation
└── NullUIService      — no-op for server/headless builds

IViewFactory (view creation + layer attachment)
├── UIToolkitViewFactory — UI Toolkit backend
└── UGUIViewFactory      — UGUI backend

IDialogService (confirm/alert/modal/builder)
├── UIToolkitDialogService — popup-based dialogs (DynamicPopup + DialogBuilder)
└── NullDialogService      — no-op for server builds

IViewServiceResolver (optional service lookup from views)

SceneViewScopeService (scene-lifetime view registration)
└── ScopedViewRegistration (manual disposable scope)

TooltipService (hover tooltips on overlay layer)
TooltipManipulator (VisualElement hover behavior)
TooltipExtensions (AddTooltip / RemoveTooltip)

IView (view contract)
├── UIToolkitViewBase → UIToolkitView<TViewModel>
└── UGUIViewBase      → UGUIView<TViewModel>
```

## Assemblies

| Assembly | Engine Refs | Description |
|----------|-------------|-------------|
| **UI.Runtime** | No | Core abstractions: IView, IUIService, IDialogService, UIService, ViewModelBase |
| **UI.UIToolkit** | Yes | UI Toolkit backend: UIToolkitView, UIToolkitViewFactory, dialog views, tooltips |
| **UI.UGUI** | Yes | UGUI backend: UGUIView, UGUIViewFactory |

## Core Concepts

**IView** — View contract with `Bind`, `Show`/`Hide`, `ShowAsync`/`HideAsync`, and `Destroy`. Backend-agnostic -- no Root property at this level.

**UILayer** — Enum defining render order: `Screen`, `HUD`, `Popup`, `Overlay`. Each layer is a separate container in the UI document.

**ViewModelBase** — Disposable base class for view models. Provides `CreateProperty<T>()`, `CreateCommand()`, `CreateSubject<T>()`, and `AddDisposable()`. All created state is auto-disposed with the VM.

**UIToolkitView\<TViewModel\>** — Generic view base with typed ViewModel access and binding helpers. Manages two cleanup mechanisms: **DisposableBag** for R3 subscriptions and an unbind list for UI Toolkit events. Both are cleared automatically on hide.

## Quick Start

1. Register services in your LifetimeScope:

```csharp
// Client
builder.Register<UIToolkitViewFactory>(Lifetime.Singleton).As<IViewFactory>();
builder.Register<UIService>(Lifetime.Singleton).As<IUIService>();
builder.Register<UIToolkitDialogService>(Lifetime.Singleton).As<IDialogService>();

// Server
builder.Register<NullUIService>(Lifetime.Singleton).As<IUIService>();
builder.Register<NullDialogService>(Lifetime.Singleton).As<IDialogService>();
```

2. Register and show a view:

```csharp
await ui.Register<HudView>(UILayer.HUD);
await ui.ShowScreen<HudView>(new HudViewModel(health, ammo));
```

## Usage

### View Lifecycle

```
new() -> Root set -> OnInitialize() -> [OnBind() -> OnShowAsync() -> OnHideAsync() -> OnViewHide() -> OnUnbind()]* -> Destroy()
                     ^ once             ^ repeats per show/hide cycle
```

- `OnInitialize()` — called once after `Root` is set (during `Register`). Cache element references and animation targets here.
- `OnBind()` — called each time the view is shown with a new ViewModel. Set up bindings.
- `OnShowAsync(root, duration)` — called after display is set to Flex. Play show animations.
- `OnHideAsync(root, duration)` — called before display is set to None. Play hide animations.
- `OnViewHide()` — called after hide animation completes. Cleanup before unbind.
- `OnUnbind()` — called after all bindings are cleared. Final cleanup.

### Creating a View

```csharp
public class HudView : UIToolkitView<HudViewModel>
{
    protected override UniTask OnBind()
    {
        Bind(ViewModel.Health, h => Root.Q<Label>("hp").text = $"{h}");
        BindButton(Root.Q<Button>("reload-btn"), () => ViewModel.Reload.Execute(Unit.Default));
        return UniTask.CompletedTask;
    }

    protected override void OnViewHide() { }
    protected override void OnUnbind() { }
}
```

### Registering Views

```csharp
// Global (lives forever)
await ui.Register<LoadingScreen>(UILayer.Screen);

// Scoped (auto-unregister on dispose)
var views = new ScopedViewRegistration(ui);
await views.Register<HudView>(UILayer.HUD);
await views.Register<PausePopup>(UILayer.Popup);
// views.Dispose() unregisters all
```

### Showing and Hiding

```csharp
// Screens (one active at a time)
await ui.ShowScreen<HudView>(new HudViewModel(health, ammo));
ui.HideScreen<HudView>();                  // instant
await ui.HideScreenAsync<HudView>();       // animated
ui.HideAllScreens();

// Popups (stacked)
await ui.ShowPopup<PausePopup>(new PauseViewModel(onResume, onSettings, onQuit));
ui.HidePopup<PausePopup>();                // instant
await ui.HidePopupAsync<PausePopup>();     // animated
ui.HideTopPopup();                         // instant, topmost
await ui.HideTopPopupAsync();              // animated, topmost
```

### Binding Helpers

| Helper | Description | Cleanup |
|--------|-------------|---------|
| `Bind<T>(Observable<T>, Action<T>)` | One-way: ViewModel to UI | DisposableBag (R3) |
| `BindTextField(TextField, ReactiveProperty<string>)` | Two-way | DisposableBag + unbind list |
| `BindSlider(Slider, ReactiveProperty<float>)` | Two-way | DisposableBag + unbind list |
| `BindToggle(Toggle, ReactiveProperty<bool>)` | Two-way | DisposableBag + unbind list |
| `BindDropdown(DropdownField, ReactiveProperty<int>, choices)` | Two-way | DisposableBag + unbind list |
| `BindButton(Button, Action)` | Click handler | unbind list |
| `BindValueChanged<TElement, TValue>(element, handler)` | Value change | unbind list |
| `TrackUnbind(Action)` | Manual cleanup | unbind list |

### Creating a ViewModel

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

### ViewModel Helpers

| Helper | Returns | Use case |
|--------|---------|----------|
| `CreateProperty<T>(initial)` | `ReactiveProperty<T>` | Observable state with current value |
| `CreateCommand(action?)` | `ReactiveCommand` | UI action (button click) |
| `CreateCommand<T>(action?)` | `ReactiveCommand<T>` | UI action with payload |
| `CreateSubject<T>()` | `Subject<T>` | One-shot event, no stored value |
| `AddDisposable(disposable)` | -- | Manual disposal tracking |

### Dialogs

Standard confirm / alert / modal helpers:

```csharp
bool confirmed = await dialogs.ShowConfirm("Exit", "Are you sure?", "Quit", "Cancel");
await dialogs.ShowAlert("Error", message);
using var modal = dialogs.ShowModal("Loading", "Please wait...");
```

Custom dialogs via `DialogBuilder` (UIToolkit backend only):

```csharp
var dialogs = (UIToolkitDialogService)dialogService;

var result = await dialogs.CreateDialog("Rename")
    .WithMessage("Enter a new name:")
    .WithInput(placeholder: "name", defaultValue: currentName)
    .AddButton("Cancel", "cancel")
    .AddButton("Save", "save", isPrimary: true)
    .ShowAsync();

if (result.ButtonId == "save")
    rename(result.InputValue);
```

Supported builder options: `WithMessage`, `WithImage(Texture2D)`, `WithContent(Func<VisualElement>)`, `WithInput`, `AddButton(text, id, isPrimary)`. Pressing **Esc** completes the dialog with the last button (or an empty result if no buttons were added).

Styling classes used by `DynamicPopup` are exposed through static `DialogStyle` fields (`Overlay`, `Panel`, `Title`, `Image`, `Message`, `Content`, `Input`, `Buttons`, `Button`, `ButtonPrimary`). Override them before showing the first dialog if you need different CSS hooks.

### Scene-scoped registration

When multiple views belong to a scene, use `SceneViewScopeService` to auto-unregister them on scene exit:

```csharp
public class GameplayScene : IDisposable
{
    private readonly ScopedViewRegistration _views;

    public GameplayScene(SceneViewScopeService scope)
    {
        _views = scope.Begin();  // disposes previous scope if any
    }

    public async UniTask Load()
    {
        await _views.Register<HudView>(UILayer.HUD);
        await _views.Register<PausePopup>(UILayer.Popup);
    }

    public void Dispose() => _views.Dispose();  // unregisters both views
}
```

Calling `Begin()` again disposes the previous scope automatically — one active scope per service.

### Service resolution

Views access services through `IViewServiceResolver` (implemented as an adapter over your DI container):

```csharp
public class VContainerServiceResolver : IViewServiceResolver
{
    private readonly IObjectResolver _container;
    public VContainerServiceResolver(IObjectResolver container) => _container = container;
    public T? Resolve<T>() where T : class => _container.Resolve<T>();
}
```

Register it once in your DI setup. Inside views:

```csharp
protected override UniTask OnBind()
{
    var audio = GetService<IAudioService>();   // throws if not registered
    audio.Play("hover");
    return UniTask.CompletedTask;
}
```

`GetService<T>` calls `IViewServiceResolver.Require<T>()` — throws `InvalidOperationException` if the service is missing. Use `Resolver.Resolve<T>()` directly when a null return is acceptable.

### Tooltips

Show tooltips on hover for any `VisualElement`. Supports plain text and rich content. Works with both UI elements and 3D objects (via screen position).

**Setup:**

```csharp
var tooltipService = new TooltipService(uiDocument);
// Optionally pass a StyleSheet for default tooltip styles:
// var tooltipService = new TooltipService(uiDocument, tooltipStyleSheet);
```

**UI elements — via Manipulator or extension method:**

```csharp
// Extension method (recommended)
element.AddTooltip(tooltipService, "Tooltip text");
element.AddTooltip(tooltipService, "Tooltip text", delay: 0.5f);

// Rich content
element.AddTooltip(tooltipService, () => {
    var el = new VisualElement();
    el.Add(new Label("Title"));
    el.Add(new Label("Description"));
    return el;
});

// Remove tooltip
var manipulator = element.AddTooltip(tooltipService, "Text");
element.RemoveTooltip(manipulator);

// Or directly via Manipulator
element.AddManipulator(new TooltipManipulator(tooltipService, "Text"));
```

**3D objects — via service directly:**

```csharp
// Show at screen position (e.g. from a raycast)
tooltipService.Show(Input.mousePosition, "Object name");

// Update position each frame
tooltipService.UpdatePosition(Input.mousePosition);

// Hide
tooltipService.Hide();
```

**Styling:** The tooltip container uses CSS classes `.tooltip-container` and `.tooltip-text`. Define these in your project's theme USS:

```css
.tooltip-container {
    background-color: var(--color-bg);
    border-color: var(--color-border);
    border-width: 1px;
    border-radius: 8px;
    padding: 6px 10px;
    max-width: 300px;
}

.tooltip-text {
    color: var(--color-text);
    font-size: 14px;
    white-space: normal;
}
```

### Animations

Views show/hide instantly by default. Override `OnShowAsync`/`OnHideAsync` to add transitions.

```csharp
public class PausePopup : UIToolkitView<PauseViewModel>
{
    protected override UniTask OnShowAsync(IAnimationTarget root, float duration)
        => ViewAnimations.FadeAndScale.PlayShowAsync(root, duration);

    protected override UniTask OnHideAsync(IAnimationTarget root, float duration)
        => ViewAnimations.Fade.PlayHideAsync(root, duration);
}
```

Per-element animations use cached **UIToolkitAnimationTarget** instances:

```csharp
private UIToolkitAnimationTarget _overlay = default!;
private UIToolkitAnimationTarget _panel = default!;

protected override void OnInitialize()
{
    _overlay = new UIToolkitAnimationTarget(Root.Q(className: "overlay"));
    _panel = new UIToolkitAnimationTarget(Root.Q(className: "panel"));
}

protected override async UniTask OnShowAsync(IAnimationTarget root, float duration)
{
    await UniTask.WhenAll(
        ViewAnimations.Fade.PlayShowAsync(_overlay, duration),
        ViewAnimations.FadeAndScale.PlayShowAsync(_panel, duration));
}
```

Custom animations implement **IViewAnimation**:

```csharp
public class BounceAnimation : IViewAnimation
{
    public async UniTask PlayShowAsync(IAnimationTarget target, float duration) { /* LitMotion */ }
    public async UniTask PlayHideAsync(IAnimationTarget target, float duration) { /* LitMotion */ }
}
```

### Cursor Visibility

**UIService** exposes `SetVisibilityCallback(Action<bool>)` to notify when UI is shown or fully hidden. Wire in your DI registration:

```csharp
builder.RegisterBuildCallback(resolver =>
{
    var ui = (UIService)resolver.Resolve<IUIService>();
    var cursor = resolver.Resolve<ICursorService>();
    ui.SetVisibilityCallback(visible => cursor.SetVisible(visible));
});
```

## Design Decisions

- **IView has no Root** — keeps the interface backend-agnostic. **UIToolkitViewBase** adds `VisualElement Root`, **UGUIViewBase** adds its own root.
- **IViewFactory owns all DOM operations** — creation, UXML loading, layer attachment. Views do not manage their own DOM placement.
- **UIService is backend-agnostic** — delegates to **IViewFactory**, uses `Action<bool>` callback for cursor state instead of depending on a cursor service.
- **IDialogService is separate from IUIService** — modals are implemented as a single popup view (**DynamicPopup**) driven by **DialogBuilder** + **DynamicDialogViewModel**, not inline VisualElements.
- **UxmlLoader delegate instead of IAssetService** — **UIToolkitViewFactory** takes a `UxmlLoader` delegate, avoiding a hard dependency on any asset loading strategy.

## File Structure

```
com.rubickanov.ui/
├── Runtime/
│   ├── IView.cs
│   ├── IUIService.cs
│   ├── IViewFactory.cs
│   ├── IDialogService.cs
│   ├── IViewAnimation.cs
│   ├── IAnimationTarget.cs
│   ├── IViewServiceResolver.cs
│   ├── NoneAnimation.cs
│   ├── UIService.cs
│   ├── ViewModelBase.cs
│   ├── UILayer.cs
│   ├── ScopedViewRegistration.cs
│   ├── SceneViewScopeService.cs
│   ├── NullUIService.cs
│   └── NullDialogService.cs
├── UIToolkit/
│   ├── UIToolkitViewBase.cs
│   ├── UIToolkitView.cs
│   ├── UIToolkitViewFactory.cs
│   ├── UIToolkitAnimationTarget.cs
│   ├── UIToolkitDialogService.cs
│   ├── DialogBuilder.cs
│   ├── DialogResult.cs
│   ├── DialogStyle.cs
│   ├── DynamicPopup.cs
│   ├── DynamicDialogViewModel.cs
│   └── Tooltip/
│       ├── TooltipService.cs
│       ├── TooltipManipulator.cs
│       ├── TooltipExtensions.cs
│       └── Tooltip.uss
└── UGUI/
    ├── UGUIViewBase.cs
    ├── UGUIView.cs
    ├── UGUIViewFactory.cs
    └── UGUIAnimationTarget.cs
```
