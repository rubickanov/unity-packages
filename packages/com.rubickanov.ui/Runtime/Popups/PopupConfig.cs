using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>What a <see cref="PopupBuilder"/> has collected: content, placement, behaviour and close rules.</summary>
    internal sealed class PopupConfig
    {
        // ── Content ──────────────────────────────────────────────
        public string? Title;
        public string? Message;
        public Texture2D? Icon;

        /// <summary>Arbitrary content appended below the message.</summary>
        public Func<VisualElement>? ContentFactory;

        /// <summary>
        /// A registered view shown below the message (and below <see cref="ContentFactory"/>'s content): a new
        /// instance built from its UXML, bound to <see cref="ContentViewModel"/>. The popup owns both.
        /// </summary>
        public Type? ContentViewType;

        /// <summary>The view model of <see cref="ContentViewType"/>.</summary>
        public ViewModelBase? ContentViewModel;

        public bool HasInput;
        public string InputPlaceholder = "";
        public string InputDefault = "";

        public readonly List<PopupButton> Buttons = new();

        // ── Layout & behaviour ───────────────────────────────────
        public PopupPlacement Placement = PopupPlacement.ScreenCenter();
        public PopupBehaviour Behaviour = PopupBehaviour.Passive;
        public PopupCloseTriggers CloseTriggers = PopupCloseTriggers.None;

        /// <summary>Seconds before the popup closes by itself; zero never.</summary>
        public float TimeoutSeconds;

        /// <summary>Layer to attach to; null: the overlay layer for a modal popup, the popup layer otherwise.</summary>
        public UILayer? Layer;

        /// <summary>Optional stylesheet applied to this popup only.</summary>
        public StyleSheet? StyleSheet;

        /// <summary>Extra USS classes added to the panel root.</summary>
        public readonly List<string> Classes = new();

        /// <summary>Close every other open popup before showing this one.</summary>
        public bool DismissOthers;

        /// <summary>Played on the panel on open and close. Null: the content view's own, else the host default.</summary>
        public IViewAnimation? Animation;

        public UILayer ResolveLayer() => Layer ?? (Behaviour == PopupBehaviour.Modal ? UILayer.Overlay : UILayer.Popup);
    }

    /// <summary>A button in the popup's button row. Clicking it closes the popup with its id.</summary>
    public readonly struct PopupButton
    {
        public readonly string Text;
        public readonly string Id;
        public readonly bool IsPrimary;

        public PopupButton(string text, string id, bool isPrimary = false)
        {
            Text = text;
            Id = id;
            IsPrimary = isPrimary;
        }
    }
}
