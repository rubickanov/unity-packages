using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    /// <summary>
    /// Full description of a popup: content, placement, behaviour and close rules.
    /// Usually built through <see cref="PopupBuilder"/> rather than constructed directly.
    /// </summary>
    public sealed class PopupConfig
    {
        // ── Content ──────────────────────────────────────────────
        public string? Title;
        public string? Message;
        public Texture2D? Icon;

        /// <summary>Arbitrary content appended below the message.</summary>
        public Func<VisualElement>? ContentFactory;

        public bool HasInput;
        public string InputPlaceholder = "";
        public string InputDefault = "";

        public readonly List<PopupButton> Buttons = new();

        // ── Layout & behaviour ───────────────────────────────────
        public PopupPlacement Placement = PopupPlacement.ScreenCenter();
        public PopupBehaviour Behaviour = PopupBehaviour.Passive;
        public PopupCloseTriggers CloseTriggers = PopupCloseTriggers.None;

        /// <summary>Seconds before auto-close. Only used when <see cref="PopupCloseTriggers.Timeout"/> is set.</summary>
        public float TimeoutSeconds;

        /// <summary>Layer to attach to. Modal popups default to <see cref="UILayer.Overlay"/>.</summary>
        public UILayer Layer = UILayer.Popup;

        /// <summary>Optional stylesheet applied to this popup only.</summary>
        public StyleSheet? StyleSheet;

        /// <summary>Extra USS class added to the panel root, for theme variants.</summary>
        public string? RootClass;

        /// <summary>Close every other open popup before showing this one.</summary>
        public bool DismissOthers;
    }

    /// <summary>A button rendered in the popup's button row.</summary>
    public readonly struct PopupButton
    {
        public readonly string Text;
        public readonly string Id;
        public readonly bool IsPrimary;

        /// <summary>Whether clicking this button closes the popup (default true).</summary>
        public readonly bool ClosesOnClick;

        public PopupButton(string text, string id, bool isPrimary = false, bool closesOnClick = true)
        {
            Text = text;
            Id = id;
            IsPrimary = isPrimary;
            ClosesOnClick = closesOnClick;
        }
    }
}
