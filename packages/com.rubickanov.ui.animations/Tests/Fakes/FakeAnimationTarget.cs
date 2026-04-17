using Rubickanov.UI;

namespace Rubickanov.UI.Animations.Tests
{
    internal sealed class FakeAnimationTarget : IAnimationTarget
    {
        public float Opacity { get; set; } = 1f;
        public float TranslateX { get; set; }
        public float TranslateY { get; set; }
        public float ScaleX { get; set; } = 1f;
        public float ScaleY { get; set; } = 1f;
        public bool Visible { get; private set; } = true;
        public int ResetCount { get; private set; }

        public void SetVisible(bool visible) => Visible = visible;

        public void ResetAnimationState()
        {
            Opacity = 1f;
            TranslateX = 0f;
            TranslateY = 0f;
            ScaleX = 1f;
            ScaleY = 1f;
            ResetCount++;
        }
    }
}
