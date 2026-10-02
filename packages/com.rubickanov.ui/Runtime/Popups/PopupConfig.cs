using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>What a <see cref="PopupBuilder"/> has collected: content, placement, behaviour and close rules.</summary>
    internal sealed class PopupConfig
    {
        /// <summary>Content built when the popup opens. Exclusive with <see cref="ContentViewType"/>.</summary>
        public Func<VisualElement>? ContentFactory;

        /// <summary>
        /// A registered view shown in the panel: a new instance built from its UXML, bound to
        /// <see cref="ContentViewModel"/>. The popup owns both.
        /// </summary>
        public Type? ContentViewType;

        /// <summary>The view model of <see cref="ContentViewType"/>.</summary>
        public ViewModelBase? ContentViewModel;

        /// <summary>
        /// Set when a popup took <see cref="ContentViewModel"/>: it is disposed with that popup, so it cannot open
        /// another.
        /// </summary>
        public bool ContentViewModelTaken;

        /// <summary>Clicks pass through the panel to what is below, and the popup holds no pointer capture.</summary>
        public bool PassThrough;

        public PopupPlacement Placement = PopupPlacement.ScreenCenter();
        public PopupBehaviour Behaviour = PopupBehaviour.Passive;
        public PopupCloseTriggers CloseTriggers = PopupCloseTriggers.None;

        /// <summary>Layer to attach to; null: the overlay layer for a modal popup, the popup layer otherwise.</summary>
        public UILayer? Layer;

        /// <summary>Optional stylesheet applied to this popup only.</summary>
        public StyleSheet? StyleSheet;

        /// <summary>Extra USS classes added to the panel.</summary>
        public readonly List<string> Classes = new();

        /// <summary>Close every other open popup before showing this one.</summary>
        public bool DismissOthers;

        /// <summary>Played on open and close. Null: the content view's own, else the host default.</summary>
        public IViewAnimation? Animation;

        public UILayer ResolveLayer() => Layer ?? (Behaviour == PopupBehaviour.Modal ? UILayer.Overlay : UILayer.Popup);
    }
}
