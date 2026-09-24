using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// Describes a popup: content, placement, behaviour and close rules. Start one with
    /// <see cref="IPopupService.Create"/>; <see cref="Open"/> shows it and returns its handle.
    /// </summary>
    public sealed class PopupBuilder
    {
        private readonly IPopupService _service;

        internal PopupConfig Config { get; } = new();

        public PopupBuilder(IPopupService service) => _service = service ?? throw new ArgumentNullException(nameof(service));

        public PopupBuilder Title(string title) { Config.Title = title; return this; }
        public PopupBuilder Message(string message) { Config.Message = message; return this; }
        public PopupBuilder Icon(Texture2D icon) { Config.Icon = icon; return this; }

        /// <summary>Arbitrary content below the message, built when the popup opens.</summary>
        public PopupBuilder Content(Func<VisualElement> factory) { Config.ContentFactory = factory; return this; }

        /// <summary>
        /// Shows a new instance of the registered view <typeparamref name="TView"/>, bound to
        /// <paramref name="viewModel"/>, below the message. The popup owns both: the view is destroyed and the view
        /// model disposed when the popup's elements are removed. Open such a builder once.
        /// </summary>
        public PopupBuilder Content<TView>(ViewModelBase viewModel) where TView : View
        {
            Config.ContentViewType = typeof(TView);
            Config.ContentViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            return this;
        }

        public PopupBuilder Input(string placeholder = "", string defaultValue = "")
        {
            Config.HasInput = true;
            Config.InputPlaceholder = placeholder;
            Config.InputDefault = defaultValue;
            return this;
        }

        /// <summary>A button that closes the popup with <paramref name="id"/> as <see cref="PopupResult.ButtonId"/>.</summary>
        public PopupBuilder Button(string text, string id, bool isPrimary = false)
        {
            Config.Buttons.Add(new PopupButton(text, id, isPrimary));
            return this;
        }

        public PopupBuilder At(in PopupPlacement placement) { Config.Placement = placement; return this; }

        /// <summary>Adds a backdrop. Puts the popup on the overlay layer unless <see cref="OnLayer"/> names one.</summary>
        public PopupBuilder Modal(bool modal = true)
        {
            Config.Behaviour = modal ? PopupBehaviour.Modal : PopupBehaviour.Passive;
            return this;
        }

        public PopupBuilder CloseOn(PopupCloseTriggers triggers) { Config.CloseTriggers |= triggers; return this; }

        /// <summary>Closes the popup with <see cref="PopupCloseReason.Timeout"/> after <paramref name="seconds"/>.</summary>
        public PopupBuilder Timeout(float seconds) { Config.TimeoutSeconds = seconds; return this; }

        /// <summary>Default: the overlay layer for a modal popup, the popup layer otherwise.</summary>
        public PopupBuilder OnLayer(UILayer layer) { Config.Layer = layer; return this; }

        public PopupBuilder Style(StyleSheet sheet) { Config.StyleSheet = sheet; return this; }

        /// <summary>Adds a USS class to the panel root, for theme variants.</summary>
        public PopupBuilder Class(string className) { Config.Classes.Add(className); return this; }

        /// <summary>Closes every other open popup, with <see cref="PopupCloseReason.Replaced"/>, before this one opens.</summary>
        public PopupBuilder DismissOthers(bool dismiss = true) { Config.DismissOthers = dismiss; return this; }

        /// <summary>Played on the panel. Default: the content view's own animation, else the <see cref="PopupHost"/> one.</summary>
        public PopupBuilder Animation(IViewAnimation animation) { Config.Animation = animation; return this; }

        /// <summary>Shows the popup.</summary>
        public IPopupHandle Open() => _service.Open(this);

        /// <summary>Shows the popup and awaits its result.</summary>
        public UniTask<PopupResult> OpenAsync() => Open().Result;
    }
}
