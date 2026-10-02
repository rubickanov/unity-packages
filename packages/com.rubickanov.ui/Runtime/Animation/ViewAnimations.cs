namespace Rubickanov.UI
{
    /// <summary>The ready animations. A game's own looks implement <see cref="IViewAnimation"/> on <see cref="Tween"/>.</summary>
    public static class ViewAnimations
    {
        /// <summary>Instant show and hide.</summary>
        public static IViewAnimation None => NoneAnimation.Instance;

        /// <summary>Opacity in and out over 0.2 s of unscaled time (<see cref="FadeAnimation"/>).</summary>
        public static readonly IViewAnimation Fade = new FadeAnimation();
    }
}
