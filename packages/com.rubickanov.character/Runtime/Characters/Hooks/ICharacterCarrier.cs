using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// What carries a character's body while it is down or getting up, such as a moving deck under it. The character
    /// does not know what it is; the game sets it on <see cref="CharacterRagdoll.Carrier"/>. Where it carries nothing,
    /// the body goes with the rigidbody or moving platform under it, if any.
    /// </summary>
    public interface ICharacterCarrier
    {
        /// <summary>
        /// How the carrier moves at <paramref name="point"/>: its velocity there and its angular velocity (rad/s);
        /// false where it does not carry the point.
        /// </summary>
        bool TryMotion(Vector3 point, out Vector3 velocity, out Vector3 angularVelocity);
    }
}
