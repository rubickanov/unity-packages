using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Shared mutable state. Modules read and write fields here each frame.
    /// The simulation resets per-frame multipliers at the end of each tick.
    /// External consumers should use <see cref="IReadOnlyMotorState"/>.
    /// </summary>
    public class MotorState : IReadOnlyMotorState
    {
        // -- Input (written by simulation from MotorInput) --
        public Vector2 MoveInput;
        public bool JumpPressed;
        public bool SprintHeld;
        public bool CrouchPressed;

        // -- Ground (written by GroundDetectionModule) --
        public bool IsGrounded;
        public Vector3 GroundNormal = Vector3.up;
        public float GroundAngle;

        // -- Movement (read/written by modules) --
        public Vector3 DesiredVelocity;
        public Vector3 CurrentVelocity;
        public Vector3 ExternalForce;

        // -- Speed stacking — each module multiplies --
        public float SpeedMultiplier = 1f;
        public float GravityMultiplier = 1f;

        // -- State flags --
        public bool IsSprinting;
        public bool IsCrouching;
        public bool IsInAir;

        // -- IReadOnlyMotorState --
        Vector2 IReadOnlyMotorState.MoveInput => MoveInput;
        bool IReadOnlyMotorState.IsGrounded => IsGrounded;
        Vector3 IReadOnlyMotorState.GroundNormal => GroundNormal;
        float IReadOnlyMotorState.GroundAngle => GroundAngle;
        Vector3 IReadOnlyMotorState.DesiredVelocity => DesiredVelocity;
        Vector3 IReadOnlyMotorState.CurrentVelocity => CurrentVelocity;
        float IReadOnlyMotorState.SpeedMultiplier => SpeedMultiplier;
        float IReadOnlyMotorState.GravityMultiplier => GravityMultiplier;
        bool IReadOnlyMotorState.IsSprinting => IsSprinting;
        bool IReadOnlyMotorState.IsCrouching => IsCrouching;
        bool IReadOnlyMotorState.IsInAir => IsInAir;

        /// <summary>Applies input struct to state fields.</summary>
        public void ApplyInput(MotorInput input)
        {
            MoveInput = input.Move;
            JumpPressed = input.Jump;
            SprintHeld = input.Sprint;
            CrouchPressed = input.Crouch;
        }

        /// <summary>
        /// Called by the simulation at the end of each tick.
        /// Resets multipliers and forces that should not persist across frames.
        /// </summary>
        public void ResetPerFrame()
        {
            SpeedMultiplier = 1f;
            GravityMultiplier = 1f;
            ExternalForce = Vector3.zero;
        }
    }
}
