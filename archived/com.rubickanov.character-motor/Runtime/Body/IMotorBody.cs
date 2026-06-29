using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Abstraction over the physics body. Modules interact with the motor
    /// exclusively through this interface. Two implementations:
    /// <see cref="RigidbodyMotorBody"/> (singleplayer) and
    /// <see cref="KinematicMotorBody"/> (multiplayer, deterministic).
    ///
    /// <para>
    /// The motor does NOT own the visible transform. <see cref="SimulatedPosition"/>
    /// and <see cref="SimulatedRotation"/> are the single source of truth —
    /// consumers read them after <c>Simulate</c> and render themselves (e.g. via
    /// a LateUpdate bridge, or by publishing into a reactive aspect). The motor
    /// only writes to <see cref="Transform"/> transiently during <c>Simulate</c>
    /// as a scratchpad for <see cref="Physics.CapsuleCast"/> and friends.
    /// </para>
    /// </summary>
    public interface IMotorBody
    {
        /// <summary>
        /// Underlying transform. Exposed for modules that need the hierarchy
        /// (e.g. <c>GetComponentInParent&lt;Rigidbody&gt;()</c>) or an orientation
        /// source. Its position/rotation are only synchronized with the simulated
        /// state during <c>Simulate</c>; outside of that window, it is the
        /// consumer's responsibility to write a visual position (or not).
        /// </summary>
        Transform Transform { get; }

        /// <summary>Alias for <see cref="SimulatedPosition"/>.</summary>
        Vector3 Position { get; }

        /// <summary>Alias for <see cref="SimulatedRotation"/>.</summary>
        Quaternion Rotation { get; }

        /// <summary>
        /// Authoritative simulated position. For <see cref="KinematicMotorBody"/>
        /// this is fresh immediately after <c>Simulate</c>. For
        /// <see cref="RigidbodyMotorBody"/> it is latched at <c>BeginFrame</c>
        /// and reflects the post-physics state of the previous tick (PhysX
        /// resolves forces after <c>FixedUpdate</c> returns).
        /// </summary>
        Vector3 SimulatedPosition { get; }

        /// <summary>Authoritative simulated rotation. Same latching semantics as <see cref="SimulatedPosition"/>.</summary>
        Quaternion SimulatedRotation { get; }

        Vector3 Velocity { get; }
        float CapsuleHeight { get; }

        /// <summary>Called by simulation before modules tick. Syncs body → state.</summary>
        void BeginFrame(MotorState state, float deltaTime);

        /// <summary>Called by simulation after modules tick. Resolves physics.</summary>
        void EndFrame(MotorState state, float deltaTime);

        void AddForce(Vector3 force, ForceMode mode);
        void SetCapsuleHeight(float height);
        void MovePosition(Vector3 position);
        void Rotate(Vector3 axis, float angle, Space relativeTo);

        /// <summary>
        /// Atomically replace position, rotation and velocity. Used by consumers
        /// that own the authoritative state externally (reactive aspect, persisted
        /// state) and need to push it into the motor before the next tick.
        /// </summary>
        void Teleport(Vector3 position, Quaternion rotation, Vector3 velocity);

        bool SphereCast(Vector3 origin, float radius, Vector3 direction,
                        float distance, out RaycastHit hit);

        bool Raycast(Vector3 origin, Vector3 direction,
                     float distance, out RaycastHit hit);

        int SphereCastNonAlloc(Vector3 origin, float radius, Vector3 direction,
                               RaycastHit[] results, float distance);

        BodySnapshot SaveState();
        void RestoreState(BodySnapshot snapshot);
    }
}
