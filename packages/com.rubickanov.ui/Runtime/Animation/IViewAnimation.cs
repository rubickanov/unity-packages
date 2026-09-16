using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    public interface IViewAnimation
    {
        UniTask PlayShowAsync(VisualElement target, CancellationToken ct);
        UniTask PlayHideAsync(VisualElement target, CancellationToken ct);

        /// <summary>Clears opacity, translate and scale set by an animation.</summary>
        void Reset(VisualElement target);
    }
}
