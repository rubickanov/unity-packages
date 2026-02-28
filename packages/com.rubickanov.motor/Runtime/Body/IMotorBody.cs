using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Abstraction over the physics body. Modules interact with the motor
    /// exclusively through this interface. Two implementations:
    /// <see cref="RigidbodyMotorBody"/> (singleplayer) and
    /// <see cref="KinematicMotorBody"/> (multiplayer, deterministic).
    /// </summary>
    public interface IMotorBody
    {
        Transform Transform { get; }
        Vector3 Position { get; }
        Quaternion Rotation { get; }
        Vector3 Velocity { get; }

        /// <summary>Called by simulation before modules tick. Syncs body → state.</summary>
        void BeginFrame(MotorState state, float deltaTime);

        /// <summary>Called by simulation after modules tick. Resolves physics.</summary>
        void EndFrame(MotorState state, float deltaTime);

        void AddForce(Vector3 force, ForceMode mode);
        void SetCapsuleHeight(float height);
        void MovePosition(Vector3 position);
        void Rotate(Vector3 axis, float angle, Space relativeTo);

        bool SphereCast(Vector3 origin, float radius, Vector3 direction,
                        float distance, out RaycastHit hit);

        bool Raycast(Vector3 origin, Vector3 direction,
                     float distance, out RaycastHit hit);

        BodySnapshot SaveState();
        void RestoreState(BodySnapshot snapshot);
    }
}
