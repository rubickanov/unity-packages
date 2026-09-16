using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Animations
{
    /// <summary>
    /// Fades opacity from 0 → 1 on show, 1 → 0 on hide.
    /// </summary>
    public sealed class FadeAnimation : IViewAnimation
    {
        private readonly float _duration;
        private readonly Ease _showEase;
        private readonly Ease _hideEase;

        public FadeAnimation(float duration = 0.3f, Ease showEase = Ease.OutCubic, Ease hideEase = Ease.InCubic)
        {
            _duration = duration;
            _showEase = showEase;
            _hideEase = hideEase;
        }

        public UniTask PlayShowAsync(VisualElement target, CancellationToken ct)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            target.style.opacity = 0f;
            return LMotion.Create(0f, 1f, _duration)
                .WithEase(_showEase)
                .Bind(target, static (x, t) => t.style.opacity = x)
                .ToUniTask(ct);
        }

        public UniTask PlayHideAsync(VisualElement target, CancellationToken ct)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            return LMotion.Create(1f, 0f, _duration)
                .WithEase(_hideEase)
                .Bind(target, static (x, t) => t.style.opacity = x)
                .ToUniTask(ct);
        }

        public void Reset(VisualElement target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            target.style.opacity = StyleKeyword.Null;
        }
    }
}
