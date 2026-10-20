using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>World-space intentions supplied by the game, independent of input devices and cameras.</summary>
    public struct CharacterInput
    {
        public Vector3 Move;
        public Vector3 Look;
        /// <summary>Where the character looks, pitch included. Turns the head, not the body. Zero looks ahead.</summary>
        public Vector3 Aim;
        /// <summary>
        /// The player looks through the character's eyes; <see cref="MovementConfig.FirstPersonTurn"/> says whether
        /// the body then turns to Look at once.
        /// </summary>
        public bool FirstPerson;
        /// <summary>A press edge, not a held button. Latched until the next simulation step.</summary>
        public bool JumpPressed;
        /// <summary>Held. Releasing during ascent shortens a jump when configured; it never starts another jump.</summary>
        public bool JumpHeld;
        /// <summary>Held. Raises the ground speed limit to the sprint speed.</summary>
        public bool Sprint;
    }
}
