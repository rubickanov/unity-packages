using System;
using Cysharp.Threading.Tasks;

namespace Rubickanov.UI.Animations
{
    /// <summary>
    /// Plays multiple <see cref="IViewAnimation"/>s in parallel via <c>UniTask.WhenAll</c>.
    /// </summary>
    /// <remarks>
    /// The internal task buffer is allocated once in the constructor and reused on every call.
    /// This means a single <c>CompositeAnimation</c> instance must not be invoked reentrantly
    /// (e.g. PlayShow inside PlayShow on the same instance). The UI framework calls show/hide
    /// sequentially per view, so this constraint holds by construction.
    /// </remarks>
    public sealed class CompositeAnimation : IViewAnimation
    {
        private readonly IViewAnimation[] _animations;
        private readonly UniTask[] _buffer;

        public CompositeAnimation(params IViewAnimation[] animations)
        {
            if (animations == null) throw new ArgumentNullException(nameof(animations));
            if (animations.Length == 0)
                throw new ArgumentException("At least one animation required.", nameof(animations));

            _animations = animations;
            _buffer = new UniTask[animations.Length];
        }

        public async UniTask PlayShowAsync(IAnimationTarget target, float duration)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            for (var i = 0; i < _animations.Length; i++)
                _buffer[i] = _animations[i].PlayShowAsync(target, duration);
            await UniTask.WhenAll(_buffer);
        }

        public async UniTask PlayHideAsync(IAnimationTarget target, float duration)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            for (var i = 0; i < _animations.Length; i++)
                _buffer[i] = _animations[i].PlayHideAsync(target, duration);
            await UniTask.WhenAll(_buffer);
        }
    }
}
