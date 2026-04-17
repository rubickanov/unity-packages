using System;
using Cysharp.Threading.Tasks;
using LitMotion;

namespace Rubickanov.UI.Animations
{
    /// <summary>
    /// Uniform scale along both axes. On show, grows from <c>startScale</c> to 1;
    /// on hide, shrinks from 1 back to <c>startScale</c>.
    /// </summary>
    public sealed class ScaleAnimation : IViewAnimation
    {
        private readonly float _startScale;

        /// <param name="startScale">Scale at the start of show / end of hide. Typically between 0 and 1.</param>
        public ScaleAnimation(float startScale = 0.8f)
        {
            _startScale = startScale;
        }

        public async UniTask PlayShowAsync(IAnimationTarget target, float duration)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

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
            if (target == null) throw new ArgumentNullException(nameof(target));

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
