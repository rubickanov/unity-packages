using System;
using Cysharp.Threading.Tasks;
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

        /// <summary>Content built by <paramref name="factory"/> each time the popup opens. Replaces a view content.</summary>
        public PopupBuilder Content(Func<VisualElement> factory)
        {
            Config.ContentFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            Config.ContentViewType = null;
            Config.ContentViewModel = null;
            return this;
        }

        /// <summary>
        /// Shows a new instance of the registered view <typeparamref name="TView"/>, bound to
        /// <paramref name="viewModel"/>. The popup owns both: the view is destroyed and the view model disposed when the
        /// popup's elements are removed, so such a builder opens once. Replaces a built content.
        /// </summary>
        public PopupBuilder Content<TView, TViewModel>(TViewModel viewModel)
            where TView : View<TViewModel>
            where TViewModel : ViewModelBase
        {
            Config.ContentViewType = typeof(TView);
            Config.ContentViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Config.ContentViewModelTaken = false;
            Config.ContentFactory = null;
            return this;
        }

        /// <summary>
        /// Lets clicks pass through the panel to whatever is below, and holds no pointer capture: a notice that only
        /// tells. Without it a popup takes the clicks on its panel and frees the pointer.
        /// </summary>
        public PopupBuilder PassThrough(bool passThrough = true) { Config.PassThrough = passThrough; return this; }

        public PopupBuilder At(in PopupPlacement placement) { Config.Placement = placement; return this; }

        /// <summary>
        /// Adds a backdrop that blocks input below, and stops <see cref="IUIService.Back"/> at the popup. Puts the
        /// popup on the overlay layer unless <see cref="OnLayer"/> names one.
        /// </summary>
        public PopupBuilder Modal(bool modal = true)
        {
            Config.Behaviour = modal ? PopupBehaviour.Modal : PopupBehaviour.Passive;
            return this;
        }

        public PopupBuilder CloseOn(PopupCloseTriggers triggers) { Config.CloseTriggers |= triggers; return this; }

        /// <summary>Default: the overlay layer for a modal popup, the popup layer otherwise.</summary>
        public PopupBuilder OnLayer(UILayer layer) { Config.Layer = layer; return this; }

        public PopupBuilder Style(StyleSheet sheet) { Config.StyleSheet = sheet; return this; }

        /// <summary>Adds a USS class to the panel, for theme variants.</summary>
        public PopupBuilder Class(string className) { Config.Classes.Add(className); return this; }

        /// <summary>Closes every other open popup, with <see cref="PopupCloseReason.Replaced"/>, before this one opens.</summary>
        public PopupBuilder DismissOthers(bool dismiss = true) { Config.DismissOthers = dismiss; return this; }

        /// <summary>
        /// Played on the panel. Default: the content view's own animation (on its <c>AnimationTarget</c>), else the
        /// <see cref="PopupHost"/> one.
        /// </summary>
        public PopupBuilder Animation(IViewAnimation animation) { Config.Animation = animation; return this; }

        /// <summary>Shows the popup.</summary>
        /// <exception cref="InvalidOperationException">A popup already took this builder's content view model.</exception>
        public IPopupHandle Open() => _service.Open(this);

        /// <summary>Shows the popup and awaits its result.</summary>
        public UniTask<PopupResult> OpenAsync() => Open().Result;
    }
}
