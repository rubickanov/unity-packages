using System;
using Cysharp.Threading.Tasks;

namespace Rubickanov.UI.UIToolkit
{
    /// <summary>Controls a single open popup.</summary>
    public interface IPopupHandle
    {
        /// <summary>False once the popup has closed.</summary>
        bool IsOpen { get; }

        /// <summary>Completes when the popup closes, carrying the reason and any button/input.</summary>
        UniTask<PopupResult> Result { get; }

        /// <summary>Closes the popup. No-op if already closed.</summary>
        void Close(string? buttonId = null, PopupCloseReason reason = PopupCloseReason.Code);

        /// <summary>Mutates the live content (title/message/icon or the raw panel).</summary>
        void UpdateContent(Action<PopupContentContext> mutate);

        /// <summary>Re-anchors the popup to a new placement.</summary>
        void SetPlacement(in PopupPlacement placement);
    }
}
