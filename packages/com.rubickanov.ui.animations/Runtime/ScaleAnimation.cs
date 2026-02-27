using Cysharp.Threading.Tasks;
using LitMotion;

namespace Rubickanov.UI.Animations
{
    public sealed class ScaleAnimation : IViewAnimation
    {
        private readonly float _startScale;

        public ScaleAnimation(float startScale = 0.8f)
        {
            _startScale = startScale;
        }

        public async UniTask PlayShowAsync(IAnimationTarget target, float duration)
        {
            target.ScaleX = _startScale;
            target.ScaleY = _startScale;
            await LMotion.Create(_startScale, 1f, duration)
                .WithEase(Ease.OutCubic)
                .Bind(target, static (x, t) =>
                {
                    t.ScaleX = x;
                    t.ScaleY = x;
                });
        }

        public async UniTask PlayHideAsync(IAnimationTarget target, float duration)
        {
            await LMotion.Create(1f, _startScale, duration)
                .WithEase(Ease.InCubic)
                .Bind(target, static (x, t) =>
                {
                    t.ScaleX = x;
                    t.ScaleY = x;
                });
        }
    }
}
