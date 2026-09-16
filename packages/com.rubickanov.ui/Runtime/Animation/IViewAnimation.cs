using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public interface IViewAnimation
    {
        UniTask PlayShowAsync(IAnimationTarget target, float duration);
        UniTask PlayHideAsync(IAnimationTarget target, float duration);
    }
}
