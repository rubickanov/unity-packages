using System;
using UnityEngine;

namespace Rubickanov.Motor.Modules
{
    /// <summary>
    /// Applies the final physics resolution: ground acceleration/deceleration,
    /// air control, gravity, and external forces. Runs last among all modules.
    /// </summary>
    [Serializable]
    public class PhysicsResolverModule : MotorModuleBase
    {
        [Header("Acceleration")]
        [SerializeField] private float _acceleration = 80f;
        [SerializeField] private float _deceleration = 120f;
        [SerializeField] private float _airControlForce = 4f;
        [SerializeField] private float _maxAirSpeed = 8f;

        [Header("Gravity")]
        [SerializeField] private float _gravity = 28f;
        [SerializeField] private float _fallMultiplier = 2.2f;

        public override int Priority => 1000;

        public override void Simulate(float deltaTime)
        {
            // External forces always apply, even when default physics is skipped
            if (State.ExternalForce.sqrMagnitude > 0.001f)
                Body.AddForce(State.ExternalForce, ForceMode.VelocityChange);

            if (State.SkipDefaultPhysics) return;

            Vector3 target = State.DesiredVelocity * State.SpeedMultiplier;

            if (State.IsGrounded)
            {
                // Project onto ground plane — prevents jittering on slopes
                target = Vector3.ProjectOnPlane(target, State.GroundNormal);

                Vector3 currentHorizontal = new Vector3(Body.Velocity.x, 0f, Body.Velocity.z);
                Vector3 diff = target - currentHorizontal;

                // Counter-movement: brake aggressively when no input
                float accel = State.MoveInput.sqrMagnitude < 0.01f
                    ? _deceleration
                    : _acceleration;

                diff = Vector3.ClampMagnitude(diff, accel * deltaTime);
                Body.AddForce(diff, ForceMode.VelocityChange);
            }
            else
            {
                // Air control
                Vector3 airForce = target * _airControlForce;
                Vector3 horizontal = new Vector3(Body.Velocity.x, 0f, Body.Velocity.z);

                if (horizontal.magnitude < _maxAirSpeed)
                    Body.AddForce(airForce, ForceMode.Acceleration);
            }

            // Gravity
            if (!State.IsGrounded)
            {
                float g = _gravity * State.GravityMultiplier;

                // Extra gravity while falling — makes descent feel snappier
                if (Body.Velocity.y < 0f)
                    g *= _fallMultiplier;

                Body.AddForce(Vector3.down * g, ForceMode.Acceleration);
            }
        }
    }
}
