using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Immutable snapshot of the motor state, passed via <see cref="MotorSimulation.StateUpdated"/>.
    /// Used by external consumers (animations, UI) — not for networking.
    /// For networking, use <see cref="MotorStateSnapshot"/>.
    /// </summary>
    public readonly struct MotorSnapshot
    {
        public readonly Vector3 Velocity;
        public readonly float HorizontalSpeed;
        public readonly bool IsGrounded;
        public readonly bool IsSprinting;
        public readonly bool IsCrouching;
        public readonly Vector3 GroundNormal;
        public readonly float GroundAngle;
        public readonly bool IsSliding;

        public MotorSnapshot(MotorState state)
        {
            Velocity = state.CurrentVelocity;
            HorizontalSpeed = new Vector3(state.CurrentVelocity.x, 0f, state.CurrentVelocity.z).magnitude;
            IsGrounded = state.IsGrounded;
            IsSprinting = state.IsSprinting;
            IsCrouching = state.IsCrouching;
            GroundNormal = state.GroundNormal;
            GroundAngle = state.GroundAngle;
            IsSliding = state.IsSliding;
        }
    }
}
