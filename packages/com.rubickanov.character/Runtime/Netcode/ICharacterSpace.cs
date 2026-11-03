using UnityEngine;

namespace Rubickanov.Characters.Netcode
{
    /// <summary>A moving space a replica is shown in, e.g. this machine's copy of a vehicle's deck.</summary>
    public interface ICharacterSpace
    {
        Vector3 ToWorld(Vector3 local);

        Quaternion ToWorld(Quaternion local);

        /// <summary>The space's own velocity at a point of the world.</summary>
        Vector3 PointVelocity(Vector3 world);
    }
}
