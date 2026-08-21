using UnityEngine;

namespace Rubickanov.UI.Animations
{
    /// <summary>
    /// Factory accessors for built-in view animations. All properties return cached
    /// singleton instances — safe to use in hot paths without per-access allocations.
    /// </summary>
    public static class ViewAnimations
    {
        private static readonly ScaleAnimation _scale = new(0.8f);
        private static readonly SlideAnimation _slideFromLeft = new(SlideDirection.Left);
        private static readonly SlideAnimation _slideFromRight = new(SlideDirection.Right);
        private static readonly SlideAnimation _slideFromTop = new(SlideDirection.Top);
        private static readonly SlideAnimation _slideFromBottom = new(SlideDirection.Bottom);
        private static readonly CompositeAnimation _fadeAndScale = new(FadeAnimation.Instance, _scale);

        /// <summary>Default animation applied when a view does not specify one explicitly. Mutable singleton slot.</summary>
        public static IViewAnimation Default { get; set; } = FadeAnimation.Instance;

        // The only mutable slot on this type — every other accessor hands back a stateless
        // readonly singleton. With Domain Reload disabled in Project Settings → Enter Play
        // Mode, an assignment made by last session's bootstrap survives into the next one,
        // and a custom IViewAnimation that captured scene objects keeps them alive.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Default = FadeAnimation.Instance;

        /// <summary>Instant show/hide with no tween — only toggles animation state.</summary>
        public static IViewAnimation None => NoneAnimation.Instance;

        /// <summary>Opacity 0 → 1 on show, 1 → 0 on hide.</summary>
        public static IViewAnimation Fade => FadeAnimation.Instance;

        /// <summary>Scale 0.8 → 1 on show, 1 → 0.8 on hide. Uses <c>IAnimationTarget.ScaleX/Y</c>.</summary>
        public static IViewAnimation Scale => _scale;

        /// <summary>Slide in from left (−100 TranslateX) on show, out to left on hide.</summary>
        public static IViewAnimation SlideFromLeft => _slideFromLeft;

        /// <summary>Slide in from right (+100 TranslateX) on show, out to right on hide.</summary>
        public static IViewAnimation SlideFromRight => _slideFromRight;

        /// <summary>Slide in from top (−100 TranslateY) on show, out to top on hide.</summary>
        public static IViewAnimation SlideFromTop => _slideFromTop;

        /// <summary>Slide in from bottom (+100 TranslateY) on show, out to bottom on hide.</summary>
        public static IViewAnimation SlideFromBottom => _slideFromBottom;

        /// <summary>Composite of <see cref="Fade"/> and <see cref="Scale"/> played in parallel.</summary>
        public static IViewAnimation FadeAndScale => _fadeAndScale;

        /// <summary>Creates a composite that plays all given animations in parallel via <c>UniTask.WhenAll</c>.</summary>
        public static IViewAnimation Combine(params IViewAnimation[] animations)
            => new CompositeAnimation(animations);
    }
}
