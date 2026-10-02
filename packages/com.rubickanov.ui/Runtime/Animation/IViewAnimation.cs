using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// How a view or popup comes and goes. Build one on <see cref="Tween.Run"/>, which runs on unscaled time, so a pause
    /// menu shown at <c>Time.timeScale</c> 0 still finishes.
    /// </summary>
    public interface IViewAnimation
    {
        /// <summary>
        /// Plays the show on <paramref name="target"/>. It may start while the hide is still under way, when the view is
        /// shown again: start from where the target is, not from fully hidden, or the view flickers.
        /// </summary>
        UniTask PlayShowAsync(VisualElement target, CancellationToken ct);

        /// <summary>Plays the hide. It may start while the show is still under way: start from where the target is.</summary>
        UniTask PlayHideAsync(VisualElement target, CancellationToken ct);

        /// <summary>Clears whatever the animation set on <paramref name="target"/>: the view stands as styled.</summary>
        void Reset(VisualElement target);
    }
}
