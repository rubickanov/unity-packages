using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// Fades the target's opacity in on show and out on hide, at a steady rate on unscaled time. Shown again while
    /// fading out, it fades back in from where it is.
    /// </summary>
    public sealed class FadeAnimation : IViewAnimation
    {
        private readonly float _seconds;

        /// <param name="seconds">From fully hidden to fully shown, and back.</param>
        public FadeAnimation(float seconds = 0.2f) => _seconds = seconds;

        public async UniTask PlayShowAsync(VisualElement target, CancellationToken ct)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            // Opacity set inline means a fade is under way; none means the target is fresh from hidden.
            var from = Current(target, whenUnset: 0f);
            target.style.opacity = from;
            await Tween.Run(_seconds * (1f - from), t => target.style.opacity = from + (1f - from) * t, ct);
            target.style.opacity = StyleKeyword.Null;
        }

        public UniTask PlayHideAsync(VisualElement target, CancellationToken ct)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            var from = Current(target, whenUnset: 1f);
            return Tween.Run(_seconds * from, t => target.style.opacity = from * (1f - t), ct);
        }

        public void Reset(VisualElement target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            target.style.opacity = StyleKeyword.Null;
        }

        private static float Current(VisualElement target, float whenUnset)
        {
            var opacity = target.style.opacity;
            return opacity.keyword == StyleKeyword.Undefined ? opacity.value : whenUnset;
        }
    }
}
