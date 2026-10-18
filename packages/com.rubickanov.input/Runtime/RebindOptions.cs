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

        private readonly List<HashSet<string>[]> _apart = new();

        /// <summary>
        /// Sides the game never reads at once (on foot and in a vehicle; driving and holding on): their slots may share a
        /// key, so W walks and drives. A side names maps or actions, several split by commas: <c>"Player"</c>,
        /// <c>"Vehicle/Throttle, Vehicle/Brake"</c>. A key is Taken only by a slot no side keeps apart from its own.
        /// </summary>
        public RebindOptions Apart(params string[] sides)
        {
            if (sides == null || sides.Length < 2)
            {
                throw new ArgumentException("Name at least two sides that are never read at once", nameof(sides));
            }

            var group = new HashSet<string>[sides.Length];
            for (int i = 0; i < sides.Length; i++)
            {
                group[i] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in sides[i].Split(','))
                {
                    if (name.Trim().Length > 0)
                    {
                        group[i].Add(name.Trim());
                    }
                }
            }

            _apart.Add(group);
            return this;
        }

        /// <summary>Whether the actions <paramref name="a"/> and <paramref name="b"/> ("Map/Action") are never read at once.</summary>
        public bool AreApart(string a, string b)
        {
            foreach (HashSet<string>[] group in _apart)
            {
                int sideA = SideOf(group, a);
                int sideB = SideOf(group, b);
                if (sideA >= 0 && sideB >= 0 && sideA != sideB)
                {
                    return true;
                }
            }

            return false;
        }

        // The side naming the action or its map; -1 for none.
        private static int SideOf(HashSet<string>[] group, string action)
        {
            int slash = action.IndexOf('/');
            string map = slash > 0 ? action[..slash] : "";
            for (int i = 0; i < group.Length; i++)
            {
                if (group[i].Contains(action) || group[i].Contains(map))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
