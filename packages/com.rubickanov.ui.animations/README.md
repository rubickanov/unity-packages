# UI Animations

LitMotion-based view animations for the [UI](../com.rubickanov.ui/) package. Provides fade, scale, slide, and composite animations that work with **IAnimationTarget** (float properties, no engine references).

## Dependencies

- `com.rubickanov.ui` — `IViewAnimation`, `IAnimationTarget`, `NoneAnimation`
- `UniTask` — async/await
- `LitMotion` — tween engine

## Quick Start

Override `OnShowAsync` / `OnHideAsync` in your view and use **ViewAnimations** factory:

```csharp
public class PausePopup : UIToolkitView<PauseViewModel>
{
    protected override UniTask OnShowAsync(IAnimationTarget root, float duration)
        => ViewAnimations.FadeAndScale.PlayShowAsync(root, duration);

    protected override UniTask OnHideAsync(IAnimationTarget root, float duration)
        => ViewAnimations.Fade.PlayHideAsync(root, duration);
}
```

Animations trigger automatically when `UIService` calls `ShowAsync()` / `HideAsync()`.

## Usage

### Built-in Animations

| Factory Property | Animation | Show Ease | Hide Ease |
|------------------|-----------|-----------|-----------|
| `ViewAnimations.None` | Instant (no-op) | -- | -- |
| `ViewAnimations.Fade` | Opacity 0 to 1 | OutCubic | InCubic |
| `ViewAnimations.Scale` | Scale 0.8 to 1 | OutCubic | InCubic |
| `ViewAnimations.FadeAndScale` | Fade + Scale composite | OutCubic | InCubic |
| `ViewAnimations.SlideFromLeft` | Translate from left | OutCubic | InCubic |
| `ViewAnimations.SlideFromRight` | Translate from right | OutCubic | InCubic |
| `ViewAnimations.SlideFromTop` | Translate from top | OutCubic | InCubic |
| `ViewAnimations.SlideFromBottom` | Translate from bottom | OutCubic | InCubic |

### Custom Composites

Combine multiple animations to run in parallel via `UniTask.WhenAll`:

```csharp
var animation = ViewAnimations.Combine(
    ViewAnimations.Fade,
    new SlideAnimation(SlideDirection.Bottom, offset: 200f)
);
```

### Changing the Default Animation

`ViewAnimations.Default` is used when no explicit animation is set:

```csharp
ViewAnimations.Default = ViewAnimations.FadeAndScale;
```

### Per-Element Animations

Cache **UIToolkitAnimationTarget** instances in `OnInitialize()` for animating individual elements:

```csharp
private UIToolkitAnimationTarget _panelTarget = default!;

protected override void OnInitialize()
{
    _panelTarget = new UIToolkitAnimationTarget(Root.Q("panel"));
}

protected override async UniTask OnShowAsync(IAnimationTarget root, float duration)
{
    await UniTask.WhenAll(
        ViewAnimations.Fade.PlayShowAsync(root, duration),
        new ScaleAnimation(0.9f).PlayShowAsync(_panelTarget, duration)
    );
}
```
