using UnityEngine;

namespace Rubickanov.UI
{
    /// <summary>
    /// One frame of a menu's input, read by the game from its own actions and fed to
    /// <see cref="MenuNavigation.Update"/>.
    /// </summary>
    public struct MenuInput
    {
        /// <summary>The navigate value held now: arrows, WASD, a stick, a d-pad.</summary>
        public Vector2 Navigate;

        /// <summary>The page axis held now, -1..1: Page Up and Down, a pad's triggers. Repeats like a direction.</summary>
        public float Page;

        /// <summary>A tab press this frame: -1 for the previous tab, 1 for the next, 0 for none. Once per press.</summary>
        public int Tab;

        /// <summary>The submit button went down this frame: Enter, a pad's south button.</summary>
        public bool Submit;
    }
}
