using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Immutable snapshot of body state for prediction/reconciliation.
    /// </summary>
    public readonly struct BodySnapshot
    {
        public readonly Vector3 Position;
        public readonly Vector3 Velocity;
        public readonly Quaternion Rotation;
        public readonly float CapsuleHeight;

        public BodySnapshot(Vector3 position, Vector3 velocity, Quaternion rotation, float capsuleHeight)
        {
            Position = position;
            Velocity = velocity;
            Rotation = rotation;
            CapsuleHeight = capsuleHeight;
        }
    }
}
