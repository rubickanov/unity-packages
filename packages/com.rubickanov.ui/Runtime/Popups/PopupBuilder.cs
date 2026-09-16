using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    /// <summary>
    /// Fluent builder for a popup. Mirrors <see cref="DialogBuilder"/>; terminal <see cref="Open"/>
    /// shows the popup and returns its handle.
    /// </summary>
    public sealed class PopupBuilder
    {
        private readonly IPopupService _service;
        private readonly PopupConfig _config = new();

        internal PopupBuilder(IPopupService service) => _service = service;

        public PopupBuilder Title(string title) { _config.Title = title; return this; }
        public PopupBuilder Message(string message) { _config.Message = message; return this; }
        public PopupBuilder Icon(Texture2D icon) { _config.Icon = icon; return this; }
        public PopupBuilder Content(Func<VisualElement> factory) { _config.ContentFactory = factory; return this; }

        public PopupBuilder Input(string placeholder = "", string defaultValue = "")
        {
            _config.HasInput = true;
            _config.InputPlaceholder = placeholder;
            _config.InputDefault = defaultValue;
            return this;
        }

        public PopupBuilder Button(string text, string id, bool isPrimary = false, bool closesOnClick = true)
        {
            _config.Buttons.Add(new PopupButton(text, id, isPrimary, closesOnClick));
            return this;
        }

        public PopupBuilder At(in PopupPlacement placement) { _config.Placement = placement; return this; }
        public PopupBuilder Modal(bool modal = true)
        {
            _config.Behaviour = modal ? PopupBehaviour.Modal : PopupBehaviour.Passive;
            if (modal) _config.Layer = UILayer.Overlay;
            return this;
        }

        public PopupBuilder CloseOn(PopupCloseTriggers triggers) { _config.CloseTriggers |= triggers; return this; }
        public PopupBuilder Timeout(float seconds)
        {
            _config.TimeoutSeconds = seconds;
            _config.CloseTriggers |= PopupCloseTriggers.Timeout;
            return this;
        }

        public PopupBuilder OnLayer(UILayer layer) { _config.Layer = layer; return this; }
        public PopupBuilder Style(StyleSheet sheet) { _config.StyleSheet = sheet; return this; }
        public PopupBuilder Class(string rootClass) { _config.RootClass = rootClass; return this; }
        public PopupBuilder DismissOthers(bool dismiss = true) { _config.DismissOthers = dismiss; return this; }

        /// <summary>Builds and shows the popup.</summary>
        public IPopupHandle Open() => _service.Open(_config);

        /// <summary>Shows the popup and awaits its result.</summary>
        public UniTask<PopupResult> OpenAsync() => _service.Open(_config).Result;
    }
}
