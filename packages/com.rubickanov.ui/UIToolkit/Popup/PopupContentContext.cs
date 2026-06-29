using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    /// <summary>
    /// Handed to <see cref="IPopupHandle.UpdateContent"/> to mutate a live popup. Convenience setters
    /// update the standard title/message/icon; <see cref="Panel"/> exposes the raw element for anything else.
    /// </summary>
    public sealed class PopupContentContext
    {
        /// <summary>The popup panel root, for custom mutation.</summary>
        public VisualElement Panel { get; }

        private readonly Label? _title;
        private readonly Label? _message;
        private readonly VisualElement? _icon;

        internal PopupContentContext(VisualElement panel, Label? title, Label? message, VisualElement? icon)
        {
            Panel = panel;
            _title = title;
            _message = message;
            _icon = icon;
        }

        /// <summary>Updates the title label, if the popup has one.</summary>
        public void SetTitle(string text)
        {
            if (_title != null) _title.text = text;
        }

        /// <summary>Updates the message label, if the popup has one.</summary>
        public void SetMessage(string text)
        {
            if (_message != null) _message.text = text;
        }

        /// <summary>Updates the icon image, if the popup has one.</summary>
        public void SetIcon(Texture2D texture)
        {
            if (_icon != null) _icon.style.backgroundImage = new StyleBackground(texture);
        }
    }
}
