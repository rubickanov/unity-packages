namespace Rubickanov.UI
{
    public interface IAnimationTarget
    {
        float Opacity { get; set; }
        float TranslateX { get; set; }
        float TranslateY { get; set; }
        float ScaleX { get; set; }
        float ScaleY { get; set; }
        void SetVisible(bool visible);
        void ResetAnimationState();
    }
}
