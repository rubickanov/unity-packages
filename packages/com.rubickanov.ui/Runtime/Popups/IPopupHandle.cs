using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>Controls a single open popup. Disposing it closes the popup.</summary>
    public interface IPopupHandle : IDisposable
    {
        /// <summary>False once the popup has closed.</summary>
        bool IsOpen { get; }

        /// <summary>Completes when the popup closes, carrying the reason and any button/input.</summary>
        UniTask<PopupResult> Result { get; }

        /// <summary>The popup's panel root, for changes to its own content.</summary>
        VisualElement Panel { get; }

        /// <summary>Closes the popup. No-op if already closed.</summary>
        void Close(string? buttonId = null, PopupCloseReason reason = PopupCloseReason.Code);

        /// <summary>Changes the title, if the popup has one.</summary>
        void SetTitle(string text);

        /// <summary>Changes the message, if the popup has one.</summary>
        void SetMessage(string text);

        /// <summary>Changes the icon, if the popup has one.</summary>
        void SetIcon(Texture2D texture);

        /// <summary>Re-anchors the popup to a new placement. No-op if already closed.</summary>
        void SetPlacement(in PopupPlacement placement);
    }
}
