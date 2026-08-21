# UI Animations

LitMotion-based view animations for the [UI](../com.rubickanov.ui/) package. Implements `IViewAnimation` as fade, scale, slide, and composite tweens that drive an `IAnimationTarget` (float properties only — no engine references in the animation logic).

## Dependencies

> `UniTask` and `LitMotion` come from git URLs, not from UPM — UPM will not pull them in for you. See [Third-party dependencies](https://github.com/rubickanov-org/unity-packages#third-party-dependencies).

- `com.rubickanov.ui` — base package: `IViewAnimation`, `IAnimationTarget`, `NoneAnimation`, `UIToolkitView<>`
- `UniTask` — `PlayShowAsync` / `PlayHideAsync` return `UniTask`
- `LitMotion` — tween engine (`LMotion.Create(...).Bind(...)`)

Unity 6000.0+.

## Quick Start

Override `OnShowAsync` / `OnHideAsync` in a view and return a `ViewAnimations` factory call. The base view passes its own `IAnimationTarget` (a `UIToolkitAnimationTarget` wrapping `Root`) and a duration; `ShowAsync()` / `HideAsync()` invoke these overrides whenever the UI service shows or hides the view.

```csharp
using Cysharp.Threading.Tasks;
using Rubickanov.UI;
using Rubickanov.UI.Animations;
using Rubickanov.UI.UIToolkit;

public sealed class PausePopup : UIToolkitView<PauseViewModel>
{
    protected override UniTask OnShowAsync(IAnimationTarget root, float duration)
        => ViewAnimations.FadeAndScale.PlayShowAsync(root, duration);

    protected override UniTask OnHideAsync(IAnimationTarget root, float duration)
        => ViewAnimations.Fade.PlayHideAsync(root, duration);
}
```

`PlayShowAsync` / `PlayHideAsync` return `UniTask` — you must `return`, `await`, or `.Forget()` the result, or the tween is dropped silently.

## Usage

### Built-in Animations

`ViewAnimations` exposes cached singleton instances — safe to read from hot paths without per-access allocations. Show eases use `Ease.OutCubic`, hide eases use `Ease.InCubic`.

| Property | Effect |
|----------|--------|
| `ViewAnimations.None` | No tween; resets animation state only (`NoneAnimation`) |
| `ViewAnimations.Fade` | `Opacity` 0 → 1 on show, 1 → 0 on hide |
| `ViewAnimations.Scale` | Uniform scale 0.8 → 1 on show, 1 → 0.8 on hide |
| `ViewAnimations.FadeAndScale` | `Fade` and `Scale` composited in parallel |
| `ViewAnimations.SlideFromLeft` | `TranslateX` −100 → 0 on show, reverse on hide |
| `ViewAnimations.SlideFromRight` | `TranslateX` +100 → 0 on show, reverse on hide |
| `ViewAnimations.SlideFromTop` | `TranslateY` −100 → 0 on show, reverse on hide |
| `ViewAnimations.SlideFromBottom` | `TranslateY` +100 → 0 on show, reverse on hide |

### Custom Animations

`FadeAnimation`, `ScaleAnimation`, and `SlideAnimation` are constructible directly when the cached singletons don't fit. `ScaleAnimation` takes a start scale; `SlideAnimation` takes a direction and an offset (default 100).

```csharp
var bigPop = new ScaleAnimation(startScale: 0.5f);
var slideUp = new SlideAnimation(SlideDirection.Bottom, offset: 200f);

await slideUp.PlayShowAsync(root, duration);
```

### Composites

`ViewAnimations.Combine` (or `new CompositeAnimation(...)`) runs several animations in parallel via `UniTask.WhenAll`:

```csharp
IViewAnimation entrance = ViewAnimations.Combine(
    ViewAnimations.Fade,
    new SlideAnimation(SlideDirection.Bottom, offset: 200f));

protected override UniTask OnShowAsync(IAnimationTarget root, float duration)
    => entrance.PlayShowAsync(root, duration);
```

A `CompositeAnimation` reuses one internal task buffer, so a single instance must not be played reentrantly — fine under the framework's sequential per-view show/hide.

### Animating Individual Elements

The `root` target wraps the whole view. To animate a child element, wrap it in a `UIToolkitAnimationTarget` (cache it in `OnInitialize`) and play against that target:

```csharp
private UIToolkitAnimationTarget _panel = default!;

protected override void OnInitialize()
    => _panel = new UIToolkitAnimationTarget(Root.Q("panel"));

protected override UniTask OnShowAsync(IAnimationTarget root, float duration)
    => UniTask.WhenAll(
        ViewAnimations.Fade.PlayShowAsync(root, duration),
        new ScaleAnimation(0.9f).PlayShowAsync(_panel, duration));
```

### Default Slot

`ViewAnimations.Default` is a mutable slot for sharing one entrance style across views. It is not applied automatically — views with no `OnShowAsync` override stay instant; you read it yourself:

```csharp
ViewAnimations.Default = ViewAnimations.FadeAndScale;

protected override UniTask OnShowAsync(IAnimationTarget root, float duration)
    => ViewAnimations.Default.PlayShowAsync(root, duration);
```
