using System;

namespace Rubickanov.Characters
{
    /// <summary>Something a CharacterAnimator can present: a local simulation or a character replicated over the network.</summary>
    public interface ICharacterMotionSource
    {
        CharacterMotion Motion { get; }
        /// <summary>
        /// Where the character looks now, in the world, pitch included; zero looks ahead. Can be newer than
        /// Motion.Aim, which a local character takes once per simulation step.
        /// </summary>
        UnityEngine.Vector3 Aim { get; }
        event Action Jumped;
        event Action Landed;
        /// <summary>Raised after every update of Motion. Time.deltaTime is the length of that update.</summary>
        event Action<CharacterMotion> MotionUpdated;
    }
}
