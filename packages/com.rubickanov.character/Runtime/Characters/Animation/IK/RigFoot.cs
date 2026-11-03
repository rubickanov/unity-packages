using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// A foot the game places in the world (<see cref="CharacterRig.LeftFoot"/>, <see cref="CharacterRig.RightFoot"/>),
    /// e.g. a drunk's catch step: the game swings <see cref="Point"/> along its arc, holds it still to plant the foot
    /// and fades <see cref="Weight"/> out to hand the foot back to the animation. The point is an animated ankle's: its
    /// height over the character's base is the foot's lift, and the ground under it is added as for an animated foot
    /// (<see cref="CharacterRig.AnimatedFoot"/> + an offset shifts a stride's plant). Default (weight 0) leaves the
    /// foot to the animation exactly as without it.
    /// </summary>
    public struct RigFoot
    {
        /// <summary>World: where the ankle goes, as an animated ankle (lift over the base, no ground).</summary>
        public Vector3 Point;
        /// <summary>0..1: from the animated foot to this one.</summary>
        public float Weight;

        public bool IsActive => Weight > 0f;
    }
}
