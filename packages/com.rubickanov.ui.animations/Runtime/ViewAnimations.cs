namespace Rubickanov.UI.Animations
{
    public static class ViewAnimations
    {
        public static IViewAnimation Default { get; set; } = FadeAnimation.Instance;
        public static IViewAnimation None => NoneAnimation.Instance;
        public static IViewAnimation Fade => FadeAnimation.Instance;
        public static IViewAnimation Scale => new ScaleAnimation(0.8f);
        public static IViewAnimation SlideFromLeft => new SlideAnimation(SlideDirection.Left);
        public static IViewAnimation SlideFromRight => new SlideAnimation(SlideDirection.Right);
        public static IViewAnimation SlideFromTop => new SlideAnimation(SlideDirection.Top);
        public static IViewAnimation SlideFromBottom => new SlideAnimation(SlideDirection.Bottom);
        public static IViewAnimation FadeAndScale => new CompositeAnimation(Fade, Scale);

        public static IViewAnimation Combine(params IViewAnimation[] animations)
            => new CompositeAnimation(animations);
    }
}
