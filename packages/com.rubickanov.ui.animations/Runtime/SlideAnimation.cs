using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine.UIElements;

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
        private readonly float _duration;
        private readonly Ease _showEase;
        private readonly Ease _hideEase;

        /// <param name="direction">Edge to slide from on show.</param>
        /// <param name="offset">Non-negative distance in pixels. Default 100.</param>
        /// <param name="duration">Duration of show and hide in seconds.</param>
        /// <param name="showEase">Ease of the show motion.</param>
        /// <param name="hideEase">Ease of the hide motion.</param>
        public SlideAnimation(SlideDirection direction, float offset = 100f, float duration = 0.3f,
            Ease showEase = Ease.OutCubic, Ease hideEase = Ease.InCubic)
        {
            if (offset < 0f)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be non-negative.");

            _direction = direction;
            _offset = offset;
            _duration = duration;
            _showEase = showEase;
            _hideEase = hideEase;
        }

        public UniTask PlayShowAsync(VisualElement target, CancellationToken ct)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            var (startX, startY) = GetOffset();
            target.style.translate = new Translate(startX, startY);

            if (startX != 0f)
            {
                return LMotion.Create(startX, 0f, _duration)
                    .WithEase(_showEase)
                    .Bind(target, static (x, t) => t.style.translate = new Translate(x, 0f))
                    .ToUniTask(ct);
            }

            return LMotion.Create(startY, 0f, _duration)
                .WithEase(_showEase)
                .Bind(target, static (y, t) => t.style.translate = new Translate(0f, y))
                .ToUniTask(ct);
        }

        public UniTask PlayHideAsync(VisualElement target, CancellationToken ct)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            var (endX, endY) = GetOffset();

            if (endX != 0f)
            {
                return LMotion.Create(0f, endX, _duration)
                    .WithEase(_hideEase)
                    .Bind(target, static (x, t) => t.style.translate = new Translate(x, 0f))
                    .ToUniTask(ct);
            }

            return LMotion.Create(0f, endY, _duration)
                .WithEase(_hideEase)
                .Bind(target, static (y, t) => t.style.translate = new Translate(0f, y))
                .ToUniTask(ct);
        }

        public void Reset(VisualElement target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            target.style.translate = StyleKeyword.Null;
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
