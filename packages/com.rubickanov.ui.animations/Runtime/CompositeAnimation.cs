using Cysharp.Threading.Tasks;

namespace Rubickanov.UI.Animations
{
    public sealed class CompositeAnimation : IViewAnimation
    {
        private readonly IViewAnimation[] _animations;

        public CompositeAnimation(params IViewAnimation[] animations)
        {
            _animations = animations;
        }

        public async UniTask PlayShowAsync(IAnimationTarget target, float duration)
        {
            var tasks = new UniTask[_animations.Length];
            for (var i = 0; i < _animations.Length; i++)
            {
                tasks[i] = _animations[i].PlayShowAsync(target, duration);
            }
            await UniTask.WhenAll(tasks);
        }

        public async UniTask PlayHideAsync(IAnimationTarget target, float duration)
        {
            var tasks = new UniTask[_animations.Length];
            for (var i = 0; i < _animations.Length; i++)
            {
                tasks[i] = _animations[i].PlayHideAsync(target, duration);
            }
            await UniTask.WhenAll(tasks);
        }
    }
}
