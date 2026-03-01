# UI Animations

LitMotion-based view animations for the UI framework.

## Dependencies

- `UI.Runtime` — `IViewAnimation`, `IAnimationTarget`
- `UniTask` — async/await
- `LitMotion` — tween engine

Does **not** reference `UI.UIToolkit`. Animations work with `IAnimationTarget` (floats), not `VisualElement`.

## Animations

| Class | Description | Ease |
|-------|-------------|------|
| `FadeAnimation` | Opacity 0↔1 | OutCubic / InCubic |
| `ScaleAnimation` | Scale startScale↔1 (default 0.8) | OutCubic / InCubic |
| `SlideAnimation` | Translate offset↔0 (Left/Right/Top/Bottom) | OutCubic / InCubic |
| `CompositeAnimation` | `UniTask.WhenAll` on multiple animations | — |

## ViewAnimations (static factory)

```csharp
ViewAnimations.None                // NoneAnimation (instant)
ViewAnimations.Fade                // FadeAnimation singleton
ViewAnimations.Scale               // ScaleAnimation(0.8)
ViewAnimations.FadeAndScale        // Fade + Scale composite
ViewAnimations.SlideFromLeft       // SlideAnimation(Left)
ViewAnimations.SlideFromRight      // SlideAnimation(Right)
ViewAnimations.SlideFromTop        // SlideAnimation(Top)
ViewAnimations.SlideFromBottom     // SlideAnimation(Bottom)
ViewAnimations.Combine(a, b, ...)  // Custom composite
```

## Usage

Override `OnShowAsync`/`OnHideAsync` in your view:

```csharp
public class PausePopup : UIToolkitView<PauseViewModel>
{
    protected override UniTask OnShowAsync(IAnimationTarget root, float duration)
        => ViewAnimations.FadeAndScale.PlayShowAsync(root, duration);

    protected override UniTask OnHideAsync(IAnimationTarget root, float duration)
        => ViewAnimations.Fade.PlayHideAsync(root, duration);
}
```

Animations trigger automatically via `ShowAsync()`/`HideAsync()` (called by `UIService`).
