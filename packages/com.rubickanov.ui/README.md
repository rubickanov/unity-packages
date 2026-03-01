# UI Framework

Backend-agnostic UI framework with view lifecycle, layer management, and dialog system.

## Structure

```
UI/
├── Runtime/          # Core abstractions (noEngineReferences)
│   ├── IView.cs              # View contract: Bind, Show/Hide, ShowAsync/HideAsync
│   ├── IViewFactory.cs       # Backend-specific view creation + layer attachment
│   ├── IUIService.cs         # Screen/popup lifecycle, view registry
│   ├── IDialogService.cs     # Confirm/Alert/Modal dialogs
│   ├── IViewAnimation.cs     # Animation abstraction: PlayShowAsync/PlayHideAsync
│   ├── IAnimationTarget.cs   # Float-based animation target (no engine refs)
│   ├── NoneAnimation.cs      # No-op animation (instant show/hide)
│   ├── UIService.cs          # Single backend-agnostic implementation
│   ├── ViewModelBase.cs      # Disposable base for view models
│   ├── UILayer.cs            # Screen, HUD, Popup, Overlay
│   ├── ScopedViewRegistration.cs  # Auto-unregister on dispose
│   ├── NullUIService.cs      # No-op for server builds
│   └── NullDialogService.cs  # No-op for server builds
├── UIToolkit/        # UI Toolkit backend
│   ├── UIToolkitViewBase.cs   # Non-generic base: OnInitialize, ShowAsync/HideAsync
│   ├── UIToolkitView.cs       # Generic View<TViewModel> with reactive bindings
│   ├── UIToolkitViewFactory.cs # Creates views, loads UXML, calls Initialize, attaches to layers
│   ├── UIToolkitAnimationTarget.cs # Maps IAnimationTarget → VisualElement styles
│   ├── UIToolkitDialogService.cs # IDialogService via popup views
│   ├── ConfirmPopup.cs/.uxml  # Confirm dialog
│   ├── AlertPopup.cs/.uxml    # Alert dialog
│   ├── ConfirmViewModel.cs    # UniTaskCompletionSource<bool>
│   └── AlertViewModel.cs      # UniTaskCompletionSource
└── Animations/       # LitMotion-based animations (separate package)
    └── Runtime/
        ├── FadeAnimation.cs       # Opacity 0↔1
        ├── ScaleAnimation.cs      # Scale from startScale↔1
        ├── SlideAnimation.cs      # Translate from offset↔0 (4 directions)
        ├── CompositeAnimation.cs  # UniTask.WhenAll on multiple animations
        └── ViewAnimations.cs      # Static factory: Fade, Scale, FadeAndScale, etc.
```

## Assemblies

| Assembly | Namespace | Dependencies | Engine |
|---|---|---|---|
| `UI.Runtime` | `Rubickanov.UI` | UniTask | No |
| `UI.UIToolkit` | `Rubickanov.UI.UIToolkit` | UI.Runtime, UniTask, R3 | Yes |
| `UI.Animations` | `Rubickanov.UI.Animations` | UI.Runtime, UniTask, LitMotion | Yes |

## Usage

### Registering views

```csharp
// Global (lives forever)
await ui.Register<LoadingScreen>(UILayer.Screen);

// Scoped (auto-unregister on dispose)
var views = new ScopedViewRegistration(ui);
await views.Register<HudView>(UILayer.HUD);
// views.Dispose() unregisters all
```

### Creating a view

```csharp
public class MyView : UIToolkitView<MyViewModel>
{
    protected override UniTask OnBind()
    {
        Bind(ViewModel.Health, h => Root.Q<Label>("hp").text = $"{h}");
        BindButton(Root.Q<Button>("btn"), () => ViewModel.DoSomething());
        return UniTask.CompletedTask;
    }

    protected override void OnViewHide() { }  // called before unbind
    protected override void OnUnbind() { }     // cleanup subscriptions
}
```

### View lifecycle

```
new() → Root set → OnInitialize() → [OnBind() → OnShowAsync() → OnHideAsync() → OnHide()]* → Destroy()
                   ▲ once            ▲ repeats per show/hide cycle
```

- **`OnInitialize()`** — called once after `Root` is set (during `Register`). Cache element references and animation targets here.
- **`OnBind()`** — called each time the view is shown with a new ViewModel. Set up bindings.
- **`OnShowAsync(root, duration)`** — called after display is set to Flex. Play show animations.
- **`OnHideAsync(root, duration)`** — called before display is set to None. Play hide animations.
- **`OnViewHide()`** — called after hide animation completes. Cleanup before unbind.
- **`OnUnbind()`** — called after all bindings are cleared. Final cleanup.

### Binding helpers (UIToolkitView)

| Helper | Description | Cleanup |
|--------|-------------|---------|
| `Bind<T>(Observable<T>, Action<T>)` | One-way: ViewModel → UI | `DisposableBag` (R3) |
| `BindTextField(TextField, ReactiveProperty<string>)` | Two-way | `DisposableBag` + unbind list |
| `BindSlider(Slider, ReactiveProperty<float>)` | Two-way | `DisposableBag` + unbind list |
| `BindToggle(Toggle, ReactiveProperty<bool>)` | Two-way | `DisposableBag` + unbind list |
| `BindDropdown(DropdownField, ReactiveProperty<int>, choices)` | Two-way | `DisposableBag` + unbind list |
| `BindButton(Button, Action)` | Click handler | unbind list |
| `BindValueChanged<TElement, TValue>(element, handler)` | Value change | unbind list |
| `TrackUnbind(Action)` | Manual cleanup | unbind list |

Two cleanup mechanisms:
- **`DisposableBag`** — for R3 `Observable` subscriptions (returns `IDisposable`)
- **unbind list** — for UI Toolkit events (`clicked`, `RegisterValueChangedCallback`) that use `+=`/`-=` and don't return `IDisposable`

Both are cleared automatically on `Hide()`.

### Dialogs

```csharp
bool confirmed = await dialogs.ShowConfirm("Exit", "Are you sure?", "Quit", "Cancel");
await dialogs.ShowAlert("Error", message);
using var modal = dialogs.ShowModal("Loading", "Please wait...");
```

### DI wiring (VContainer)

```csharp
// Client
builder.Register<UIToolkitViewFactory>(Lifetime.Singleton).As<IViewFactory>();
builder.Register<UIService>(Lifetime.Singleton).As<IUIService>();
builder.Register<UIToolkitDialogService>(Lifetime.Singleton).As<IDialogService>();

// Server
builder.Register<NullUIService>(Lifetime.Singleton).As<IUIService>();
builder.Register<NullDialogService>(Lifetime.Singleton).As<IDialogService>();
```

## ViewModel

### Rules

- **State** — only via `CreateProperty<T>()`, `CreateCommand()`, `CreateSubject<T>()`. Auto-disposed with the VM.
- **Services in constructor** — allowed when VM consumes them directly (e.g. `SettingsViewModel` ← `IAudioService`).
- **Commands** — `CreateCommand(action)` with action passed in constructor. The creator decides what happens, VM wraps it.
- **Foreign reactive state** — expose as `ReadOnlyReactiveProperty<T>` or `Observable<T>`. Never expose someone else's writable property.
- **No View types** — VM must not reference VisualElement, MonoBehaviour, or any UI Toolkit types.
- **Escape hatch** — `AddDisposable(IDisposable)` for anything that doesn't fit the helpers above.

### Helpers (ViewModelBase)

| Helper | Returns | Use case |
|--------|---------|----------|
| `CreateProperty<T>(initial)` | `ReactiveProperty<T>` | Observable state with current value |
| `CreateCommand(action?)` | `ReactiveCommand` | UI action (button click) |
| `CreateCommand<T>(action?)` | `ReactiveCommand<T>` | UI action with payload |
| `CreateSubject<T>()` | `Subject<T>` | One-shot event, no stored value |
| `AddDisposable(disposable)` | — | Manual disposal tracking |

### Example

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

// Creator:
var vm = new PauseViewModel(ClosePause, OpenSettings, QuitGame);

// View:
BindButton(Root.Q<Button>("resume-btn"), () => ViewModel.Resume.Execute(Unit.Default));
```

### Patterns

| Pattern | When | Example |
|---------|------|---------|
| `CreateProperty<T>` | VM owns the state | `LoadingViewModel.Progress` |
| `ReadOnlyReactiveProperty<T>` (upcast) | VM exposes someone else's state | `MainMenuViewModel.Phase` (from MenuFlow) |
| `CreateCommand(action)` | UI triggers creator's logic | `PauseViewModel.Resume` |
| Service in constructor | VM delegates to infrastructure | `SettingsViewModel` ← `IAudioService` |
| Pass-through methods | Simple delegation, no state | `MainMenuViewModel.FindMatch()` |

## Animations

Views show/hide instantly by default (no override). Override `OnShowAsync`/`OnHideAsync` to add transitions.

### Simple — single animation on root

```csharp
public class PausePopup : UIToolkitView<PauseViewModel>
{
    protected override UniTask OnShowAsync(IAnimationTarget root, float duration)
        => ViewAnimations.FadeAndScale.PlayShowAsync(root, duration);

    protected override UniTask OnHideAsync(IAnimationTarget root, float duration)
        => ViewAnimations.Fade.PlayHideAsync(root, duration);
}
```

### Per-element — background + content separately

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

### Custom animation class

```csharp
public class BounceAnimation : IViewAnimation
{
    public async UniTask PlayShowAsync(IAnimationTarget target, float duration) { /* LitMotion */ }
    public async UniTask PlayHideAsync(IAnimationTarget target, float duration) { /* LitMotion */ }
}
```

### Animated vs instant hide

```csharp
_ui.HidePopup<PausePopup>();              // instant (escape key, scene transitions)
await _ui.HidePopupAsync<PausePopup>();   // animated
```

### Available presets (ViewAnimations)

| Preset | Description |
|--------|-------------|
| `None` | Instant (no animation) |
| `Fade` | Opacity 0↔1 |
| `Scale` | Scale 0.8↔1 |
| `FadeAndScale` | Fade + Scale combined |
| `SlideFromLeft/Right/Top/Bottom` | Translate from offset↔0 |
| `Combine(...)` | Parallel composition of any animations |

## Design Decisions

- **IView has no Root** — backend-agnostic. `UIToolkitViewBase` adds `VisualElement Root`.
- **IViewFactory** owns all DOM operations — creation, UXML loading, layer attachment.
- **UIService is backend-agnostic** — delegates to IViewFactory, uses `Action<bool>` callback for cursor state.
- **IDialogService is separate** — modals are views (ConfirmPopup/AlertPopup), not inline VisualElements.
- **UxmlLoader delegate** instead of IAssetService — no dependency on asset loading infrastructure.