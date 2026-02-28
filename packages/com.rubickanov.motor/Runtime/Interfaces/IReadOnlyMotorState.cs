using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Read-only view of the motor state for external consumers.
    /// Modules use <see cref="MotorState"/> directly for write access.
    /// </summary>
    public interface IReadOnlyMotorState
    {
        Vector2 MoveInput { get; }
        bool IsGrounded { get; }
        Vector3 GroundNormal { get; }
        float GroundAngle { get; }
        Vector3 DesiredVelocity { get; }
        Vector3 CurrentVelocity { get; }
        float SpeedMultiplier { get; }
        float GravityMultiplier { get; }
        bool IsSprinting { get; }
        bool IsCrouching { get; }
        bool IsInAir { get; }
    }
}
