# UI Animations

LitMotion fade, scale, slide and composite animations for views and popups. Extension for [UI](../com.rubickanov.ui/).

## Dependencies

> `UniTask` and `LitMotion` come from git URLs, not from UPM — UPM will not pull them in for you. See [Third-party dependencies](https://github.com/rubickanov/unity-packages#third-party-dependencies).

- `com.rubickanov.ui` — base package: `IViewAnimation`, `View.Animation`, `PopupHost`
- `UniTask` — animations are awaited with the view's cancellation token
- `LitMotion` — tween engine

## Quick Start

```csharp
public sealed class PauseView : View<PauseViewModel>
{
    protected override UILayer Layer => UILayer.Popup;
    protected override IViewAnimation Animation => ViewAnimations.FadeAndScale;

    protected override void OnBind() { }
}
```

The view plays `Animation` on every show and hide. Showing during a hide, or hiding during a show, cancels the running tween.

## Usage

### Ready instances

`ViewAnimations` holds shared instances with a 0.3 s duration: `None`, `Fade`, `Scale` (0.8 → 1), `SlideFromLeft`, `SlideFromRight`, `SlideFromTop`, `SlideFromBottom` (100 px), `FadeAndScale`.

### Duration, ease and offset

Duration and ease are set on the instance. Keep one instance per configuration in a static field rather than a new one per access.

```csharp
private static readonly IViewAnimation QuickFade = new FadeAnimation(0.15f);
private static readonly IViewAnimation DrawerIn = ViewAnimations.Combine(
    new SlideAnimation(SlideDirection.Right, offset: 320f, duration: 0.25f, showEase: Ease.OutQuart),
    new FadeAnimation(0.25f));

protected override IViewAnimation Animation => DrawerIn;
```

`ScaleAnimation(startScale, duration, showEase, hideEase)` scales from `startScale` to 1 on show. `Reset` clears the opacity, scale or translate the animation set, and runs when a view hides.

### Popups

```csharp
var popups = new PopupHost(root, ui, animation: ViewAnimations.Fade); // default for every popup

popups.Create()
    .Title("Hull breach")
    .Message("Deck 2 is losing pressure.")
    .Animation(new SlideAnimation(SlideDirection.Top, offset: 40f, duration: 0.2f))
    .Timeout(4f)
    .Open();
```

A `CompositeAnimation` reuses one task buffer: do not play the same instance on two targets at once.
