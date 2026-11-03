using UnityEngine;

namespace Rubickanov.Characters.Netcode
{
    /// <summary>
    /// The game's say over where a character's ragdoll is sent from and shown in, e.g. a ride the body is held on: the
    /// pose goes in that ride's space on its clock, and each machine shows it on its own copy of the ride. A component of
    /// the game on the character (any child), at most one. Without it, or with frame 0, the ragdoll goes as before: only
    /// while down, in the root's space on the owner's clock.
    /// </summary>
    public interface IRagdollNetFrame
    {
        /// <summary>Owner, each step: true sends the ragdoll's pose while the character is upright too (a live body).</summary>
        bool Streams { get; }

        /// <summary>
        /// Owner, at each pose sent: the frame it goes in, with the frame's pose on this machine and the time on its clock
        /// at this step; 0 for the root's space on the owner's clock (then the outs are ignored).
        /// </summary>
        uint FrameOf(out Vector3 position, out Quaternion rotation, out double time);

        /// <summary>
        /// Replica: this machine's copy of <paramref name="frame"/> (null for the world) and the time on that frame's
        /// clock it is shown at now; false when there is none here (yet).
        /// </summary>
        bool Resolve(uint frame, out ICharacterSpace space, out double playback);
    }
}
