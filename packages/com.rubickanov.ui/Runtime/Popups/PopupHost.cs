using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// Default <see cref="IPopupService"/>: popups on the UI layers, as many open at once as asked for, each with its
    /// pointer capture and back handler on the <see cref="IUIService"/>.
    /// </summary>
    public sealed class PopupHost : IPopupService, IDisposable
    {
        private readonly UILayerElements _layers;
        private readonly List<PopupInstance> _open = new();
        private readonly IUIService _ui;
        private readonly IDetachedViews? _views;
        private readonly IViewAnimation _defaultAnimation;
        private bool _disposed;

        /// <param name="root">Element holding the standard layer elements (for example <c>uiDocument.rootVisualElement</c>).</param>
        /// <param name="ui">
        /// Receives a pointer capture for every open modal or interactive popup and a back handler for every popup that
        /// is modal or closes on <see cref="PopupCloseTriggers.Escape"/>. A view as content needs the
        /// <see cref="UIService"/> it is registered in.
        /// </param>
        /// <param name="animation">Played by popups with no animation of their own. Default: none.</param>
        public PopupHost(VisualElement root, IUIService ui, IViewAnimation? animation = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _views = ui as IDetachedViews;
            _defaultAnimation = animation ?? NoneAnimation.Instance;
            _layers = new UILayerElements(root);
        }

        // ── IPopupService ────────────────────────────────────────

        public IPopupHandle Open(PopupBuilder popup)
        {
            if (popup == null) throw new ArgumentNullException(nameof(popup));
            if (_disposed) throw new ObjectDisposedException(nameof(PopupHost));

            var config = popup.Config;
            if (config.ContentViewModel != null)
            {
                if (config.ContentViewModelTaken)
                    throw new InvalidOperationException(
                        $"This builder's view model for {config.ContentViewType!.Name} went with the popup it opened " +
                        "and is disposed: describe every popup with a view on a new builder and a new view model.");
                config.ContentViewModelTaken = true;
            }

            if (config.DismissOthers)
                CloseAll(PopupCloseReason.Replaced);

            var instance = new PopupInstance(config, _layers[config.ResolveLayer()], this);
            _open.Add(instance);
            return instance;
        }

        public PopupBuilder Create() => new(this);

        public void CloseAll() => CloseAll(PopupCloseReason.Code);

        private void CloseAll(PopupCloseReason reason)
        {
            // Copy: Close mutates _open via the callback.
            foreach (var popup in _open.ToArray())
                popup.Close(reason);
        }

        // ── For PopupInstance ────────────────────────────────────

        internal IUIService Ui => _ui;
        internal IViewAnimation DefaultAnimation => _defaultAnimation;

        /// <summary>A new instance of a registered view, bound and shown, for the popup to own.</summary>
        internal View CreateContentView(Type viewType, ViewModelBase viewModel)
        {
            if (_views == null)
                throw new InvalidOperationException(
                    $"Popup content view {viewType.Name} needs the UIService it is registered in: this PopupHost was " +
                    $"given a {_ui.GetType().Name}.");
            return _views.CreateDetached(viewType, viewModel);
        }

        internal void OnPopupClosed(PopupInstance instance) => _open.Remove(instance);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var popup in _open.ToArray())
                popup.CloseImmediate();
        }
    }
}
