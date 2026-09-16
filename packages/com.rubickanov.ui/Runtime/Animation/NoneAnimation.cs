using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    public sealed class NoneAnimation : IViewAnimation
    {
        public static readonly NoneAnimation Instance = new();
        private NoneAnimation() { }

        public UniTask PlayShowAsync(VisualElement target, CancellationToken ct)
        {
            Reset(target);
            return UniTask.CompletedTask;
        }

        public UniTask PlayHideAsync(VisualElement target, CancellationToken ct)
            => UniTask.CompletedTask;

        public void Reset(VisualElement target)
        {
            target.style.opacity = StyleKeyword.Null;
            target.style.translate = StyleKeyword.Null;
            target.style.scale = StyleKeyword.Null;
        }
    }
}
