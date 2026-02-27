using Cysharp.Threading.Tasks;
using LitMotion;

namespace Rubickanov.UI.Animations
{
    public sealed class FadeAnimation : IViewAnimation
    {
        public static readonly FadeAnimation Instance = new();

        public async UniTask PlayShowAsync(IAnimationTarget target, float duration)
        {
            target.Opacity = 0f;
            await LMotion.Create(0f, 1f, duration)
                .WithEase(Ease.OutCubic)
                .Bind(target, static (x, t) => t.Opacity = x);
        }

        public async UniTask PlayHideAsync(IAnimationTarget target, float duration)
        {
            await LMotion.Create(1f, 0f, duration)
                .WithEase(Ease.InCubic)
                .Bind(target, static (x, t) => t.Opacity = x);
        }
    }
}
