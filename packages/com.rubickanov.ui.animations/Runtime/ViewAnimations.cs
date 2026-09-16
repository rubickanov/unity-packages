namespace Rubickanov.UI.Animations
{
    /// <summary>
    /// Ready instances of the built-in view animations with the default 0.3 s duration. All properties return
    /// cached readonly instances — safe to use in hot paths without per-access allocations. For another duration
    /// or ease, construct the animation directly (<c>new FadeAnimation(0.2f)</c>).
    /// </summary>
    public static class ViewAnimations
    {
        private static readonly FadeAnimation _fade = new();
        private static readonly ScaleAnimation _scale = new();
        private static readonly SlideAnimation _slideFromLeft = new(SlideDirection.Left);
        private static readonly SlideAnimation _slideFromRight = new(SlideDirection.Right);
        private static readonly SlideAnimation _slideFromTop = new(SlideDirection.Top);
        private static readonly SlideAnimation _slideFromBottom = new(SlideDirection.Bottom);
        private static readonly CompositeAnimation _fadeAndScale = new(_fade, _scale);

        /// <summary>Instant show/hide with no tween.</summary>
        public static IViewAnimation None => NoneAnimation.Instance;

        /// <summary>Opacity 0 → 1 on show, 1 → 0 on hide.</summary>
        public static IViewAnimation Fade => _fade;

        /// <summary>Scale 0.8 → 1 on show, 1 → 0.8 on hide.</summary>
        public static IViewAnimation Scale => _scale;

        /// <summary>Slide in from left (−100 px translate X) on show, out to left on hide.</summary>
        public static IViewAnimation SlideFromLeft => _slideFromLeft;

        /// <summary>Slide in from right (+100 px translate X) on show, out to right on hide.</summary>
        public static IViewAnimation SlideFromRight => _slideFromRight;

        /// <summary>Slide in from top (−100 px translate Y) on show, out to top on hide.</summary>
        public static IViewAnimation SlideFromTop => _slideFromTop;

        /// <summary>Slide in from bottom (+100 px translate Y) on show, out to bottom on hide.</summary>
        public static IViewAnimation SlideFromBottom => _slideFromBottom;

        /// <summary>Composite of <see cref="Fade"/> and <see cref="Scale"/> played in parallel.</summary>
        public static IViewAnimation FadeAndScale => _fadeAndScale;

        /// <summary>Creates a composite that plays all given animations in parallel via <c>UniTask.WhenAll</c>.</summary>
        public static IViewAnimation Combine(params IViewAnimation[] animations)
            => new CompositeAnimation(animations);
    }
}
