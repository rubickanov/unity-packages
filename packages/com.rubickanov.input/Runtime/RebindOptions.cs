using System;
using System.Collections.Generic;

namespace Rubickanov.Input
{
    /// <summary>
    /// What a game lets the player rebind, as data: the actions by name ("Player/Jump"), and what a slot may never take.
    /// Every binding of a listed action that is one key (a composite's part included, a stick bound whole not) becomes a
    /// slot, one per scheme. The defaults suit a keyboard-and-mouse plus pad game; most games set only the actions.
    /// </summary>
    public sealed class RebindOptions
    {
        /// <summary>Esc backs out of listening and is no key a slot takes.</summary>
        public const string DefaultCancelKey = "<Keyboard>/escape";

        /// <summary>A pad's Start backs out of listening and is no button a slot takes.</summary>
        public const string DefaultCancelButton = "<Gamepad>/start";

        /// <summary>Keys the OS or Unity needs: a keyboard slot takes any other key.</summary>
        public static readonly IReadOnlyList<string> DefaultKeysExcluded = new[]
        {
            "<Keyboard>/anyKey", "<Keyboard>/escape", "<Keyboard>/leftMeta", "<Keyboard>/rightMeta",
            "<Keyboard>/contextMenu", "<Keyboard>/imeSelected",
        };

        /// <summary>The ways of the sticks and the d-pad: a pad slot takes a button, not a direction that moves.</summary>
        public static readonly IReadOnlyList<string> DefaultPadExcluded = new[]
        {
            "<Gamepad>/leftStick/*", "<Gamepad>/rightStick/*", "<Gamepad>/dpad/*",
        };

        /// <summary>The mouse's buttons, which a keyboard slot takes beside the keys; its movement and wheel it does not.</summary>
        public static readonly IReadOnlyList<string> DefaultMouseButtons = new[]
        {
            "<Mouse>/leftButton", "<Mouse>/rightButton", "<Mouse>/middleButton", "<Mouse>/backButton",
            "<Mouse>/forwardButton",
        };

        public RebindOptions(params string[] actions)
        {
            if (actions == null || actions.Length == 0)
            {
                throw new ArgumentException("Name at least one action the player may rebind", nameof(actions));
            }

            Actions = (string[])actions.Clone();
        }

        /// <summary>The rebindable actions, "Map/Action" or "Action", in the order their slots come.</summary>
        public IReadOnlyList<string> Actions { get; }

        public string CancelKey { get; set; } = DefaultCancelKey;

        public string CancelButton { get; set; } = DefaultCancelButton;

        public IReadOnlyList<string> KeysExcluded { get; set; } = DefaultKeysExcluded;

        /// <summary>Path patterns a pad slot never takes; a trailing <c>*</c> stands for any control below.</summary>
        public IReadOnlyList<string> PadExcluded { get; set; } = DefaultPadExcluded;

        public IReadOnlyList<string> MouseButtons { get; set; } = DefaultMouseButtons;

        /// <summary>How far a pad's trigger or stick goes before it is chosen.</summary>
        public float Actuation { get; set; } = 0.5f;

        /// <summary>How long a chosen control waits for a stronger one (a stick pushed a little sideways first).</summary>
        public float SettleTime { get; set; } = 0.1f;
    }
}
