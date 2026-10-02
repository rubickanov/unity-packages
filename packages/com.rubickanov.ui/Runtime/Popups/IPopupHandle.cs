using System;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>Controls a single open popup. Disposing it closes the popup.</summary>
    public interface IPopupHandle : IDisposable
    {
        /// <summary>False once the popup has closed.</summary>
        bool IsOpen { get; }

        /// <summary>Completes when the popup closes, carrying the reason and the id it was closed with.</summary>
        UniTask<PopupResult> Result { get; }

        /// <summary>The popup's panel, holding its content.</summary>
        VisualElement Panel { get; }

        /// <summary>
        /// Closes the popup with <paramref name="id"/> as <see cref="PopupResult.Id"/>: a question's view closes it with
        /// the answer picked. Completes <see cref="Result"/> at once, then plays the hide animation. No-op if already
        /// closed.
        /// </summary>
        void Close(string? id = null);
    }
}
