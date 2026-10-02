using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Animations
{
    /// <summary>
    /// Uniform scale along both axes. On show, grows from <c>startScale</c> to 1;
    /// on hide, shrinks from 1 back to <c>startScale</c>.
    /// </summary>
    public sealed class ScaleAnimation : IViewAnimation
    {
        private readonly float _startScale;
        private readonly float _duration;
        private readonly Ease _showEase;
        private readonly Ease _hideEase;

        /// <param name="startScale">Scale at the start of show / end of hide. Typically between 0 and 1.</param>
        /// <param name="duration">Duration of show and hide in seconds.</param>
        /// <param name="showEase">Ease of the show motion.</param>
        /// <param name="hideEase">Ease of the hide motion.</param>
        public ScaleAnimation(float startScale = 0.8f, float duration = 0.3f,
            Ease showEase = Ease.OutCubic, Ease hideEase = Ease.InCubic)
        {
            _startScale = startScale;
            _duration = duration;
            _showEase = showEase;
            _hideEase = hideEase;
        }

        public UniTask PlayShowAsync(VisualElement target, CancellationToken ct)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            SetScale(target, _startScale);
            return LMotion.Create(_startScale, 1f, _duration)
                .WithEase(_showEase)
                .Bind(target, static (x, t) => SetScale(t, x))
                .ToUniTask(ct);
        }

        public UniTask PlayHideAsync(VisualElement target, CancellationToken ct)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            return LMotion.Create(1f, _startScale, _duration)
                .WithEase(_hideEase)
                .Bind(target, static (x, t) => SetScale(t, x))
                .ToUniTask(ct);
        }

        public void Reset(VisualElement target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            target.style.scale = StyleKeyword.Null;
        }

        private static void SetScale(VisualElement target, float scale)
            => target.style.scale = new Scale(new Vector3(scale, scale, 1f));
    }
}
