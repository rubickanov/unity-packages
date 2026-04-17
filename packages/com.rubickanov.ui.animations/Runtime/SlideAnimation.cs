using System;
using Cysharp.Threading.Tasks;
using LitMotion;

namespace Rubickanov.UI.Animations
{
    /// <summary>Direction from which a view slides in (and out, in reverse).</summary>
    public enum SlideDirection
    {
        Left,
        Right,
        Top,
        Bottom
    }

    /// <summary>
    /// Translates the view along a single axis. On show, slides from the direction's
    /// edge to the origin; on hide, back out to the edge.
    /// </summary>
    public sealed class SlideAnimation : IViewAnimation
    {
        private readonly SlideDirection _direction;
        private readonly float _offset;

        /// <param name="direction">Edge to slide from on show.</param>
        /// <param name="offset">Non-negative distance in translate units. Default 100.</param>
        public SlideAnimation(SlideDirection direction, float offset = 100f)
        {
            if (offset < 0f)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be non-negative.");

            _direction = direction;
            _offset = offset;
        }

        public async UniTask PlayShowAsync(IAnimationTarget target, float duration)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

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
            if (target == null) throw new ArgumentNullException(nameof(target));

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
