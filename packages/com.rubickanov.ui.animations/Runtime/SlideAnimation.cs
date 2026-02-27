using Cysharp.Threading.Tasks;
using LitMotion;

namespace Rubickanov.UI.Animations
{
    public enum SlideDirection
    {
        Left,
        Right,
        Top,
        Bottom
    }

    public sealed class SlideAnimation : IViewAnimation
    {
        private readonly SlideDirection _direction;
        private readonly float _offset;

        public SlideAnimation(SlideDirection direction, float offset = 100f)
        {
            _direction = direction;
            _offset = offset;
        }

        public async UniTask PlayShowAsync(IAnimationTarget target, float duration)
        {
            var (startX, startY) = GetOffset();
            target.TranslateX = startX;
            target.TranslateY = startY;

            if (startX != 0f)
            {
                await LMotion.Create(startX, 0f, duration)
                    .WithEase(Ease.OutCubic)
                    .Bind(target, static (x, t) => t.TranslateX = x);
            }
            else
            {
                await LMotion.Create(startY, 0f, duration)
                    .WithEase(Ease.OutCubic)
                    .Bind(target, static (y, t) => t.TranslateY = y);
            }
        }

        public async UniTask PlayHideAsync(IAnimationTarget target, float duration)
        {
            var (endX, endY) = GetOffset();

            if (endX != 0f)
            {
                await LMotion.Create(0f, endX, duration)
                    .WithEase(Ease.InCubic)
                    .Bind(target, static (x, t) => t.TranslateX = x);
            }
            else
            {
                await LMotion.Create(0f, endY, duration)
                    .WithEase(Ease.InCubic)
                    .Bind(target, static (y, t) => t.TranslateY = y);
            }
        }

        private (float x, float y) GetOffset() => _direction switch
        {
            SlideDirection.Left => (-_offset, 0f),
            SlideDirection.Right => (_offset, 0f),
            SlideDirection.Top => (0f, -_offset),
            SlideDirection.Bottom => (0f, _offset),
            _ => (0f, 0f)
        };
    }
}
