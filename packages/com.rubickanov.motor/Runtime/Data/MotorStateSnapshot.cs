using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Complete simulation state snapshot for prediction and reconciliation.
    /// Captures body state, shared motor state, and all stateful module states.
    /// </summary>
    public struct MotorStateSnapshot
    {
        public BodySnapshot Body;

        // Shared motor state
        public Vector3 DesiredVelocity;
        public Vector3 CurrentVelocity;
        public Vector3 ExternalForce;
        public Vector3 GroundNormal;
        public float GroundAngle;
        public float SpeedMultiplier;
        public float GravityMultiplier;
        public bool IsGrounded;
        public bool IsSprinting;
        public bool IsCrouching;
        public bool IsInAir;

        // Module states (serialized sequentially by priority order)
        public byte[]? ModuleStates;
    }
}
