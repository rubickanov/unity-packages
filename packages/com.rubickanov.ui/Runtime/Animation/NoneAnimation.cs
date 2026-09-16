using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public sealed class NoneAnimation : IViewAnimation
    {
        public static readonly NoneAnimation Instance = new();
        private NoneAnimation() { }

        public UniTask PlayShowAsync(IAnimationTarget target, float duration)
        {
            target.ResetAnimationState();
            return UniTask.CompletedTask;
        }

        public UniTask PlayHideAsync(IAnimationTarget target, float duration)
            => UniTask.CompletedTask;
    }
}
