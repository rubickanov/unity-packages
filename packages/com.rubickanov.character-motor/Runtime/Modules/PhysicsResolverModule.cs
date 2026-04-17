using System;
using UnityEngine;

namespace Rubickanov.Motor.Modules
{
    /// <summary>
    /// Applies the final physics resolution: ground acceleration/deceleration,
    /// air control, gravity, and external forces. Runs last among all modules.
    /// </summary>
    [Serializable]
    public class PhysicsResolverModule : MotorModuleBase, IStatefulModule
    {
        [Header("Acceleration")]
        [SerializeField] private float _acceleration = 80f;
        [SerializeField] private float _deceleration = 120f;

        [Header("Air Control")]
        [Range(0f, 1f)]
        [SerializeField] private float _airControl = 0.3f;
        [SerializeField] private float _airControlForce = 4f;
        [SerializeField] private float _maxAirSpeed = 8f;
        [SerializeField] private float _airDrag = 0.5f;

        [Header("Air Strafe (Source-style)")]
        [SerializeField] private bool _enableAirStrafe;
        [SerializeField] private float _airStrafeAccel = 50f;
        [SerializeField] private float _airStrafeMaxWishSpeed = 1f;

        [Header("Momentum")]
        [SerializeField] private bool _preserveTakeoffSpeed;

        [Header("Gravity")]
        [SerializeField] private float _gravity = 28f;
        [SerializeField] private float _fallMultiplier = 2.2f;

        public override int Priority => 1000;

        private bool _wasGrounded;
        private float _takeoffSpeed;

        public override void Simulate(float deltaTime)
        {
            // External forces always apply, even when default physics is skipped
            if (State.ExternalForce.sqrMagnitude > 0.001f)
                Body.AddForce(State.ExternalForce, ForceMode.VelocityChange);

            if (State.SkipDefaultPhysics) return;

            // Track ground→air transition for takeoff speed
            if (_wasGrounded && !State.IsGrounded)
            {
                Vector3 vel = Body.Velocity;
                _takeoffSpeed = new Vector3(vel.x, 0f, vel.z).magnitude;
            }
            _wasGrounded = State.IsGrounded;

            Vector3 target = State.DesiredVelocity * State.SpeedMultiplier;

            if (State.IsGrounded)
            {
                // Project onto ground plane — prevents jittering on slopes
                target = Vector3.ProjectOnPlane(target, State.GroundNormal);

                if (State.GroundVelocity.sqrMagnitude > 0.001f)
                    target += State.GroundVelocity;

                Vector3 currentOnGround = Vector3.ProjectOnPlane(Body.Velocity, State.GroundNormal);
                Vector3 diff = target - currentOnGround;

                // Decel when no input OR when target reduces speed along current velocity direction
                // (reversal). Accel when starting or pushing further in current direction.
                float currentSpeed = currentOnGround.magnitude;
                bool noInput = State.MoveInput.sqrMagnitude < 0.01f;
                bool targetReducesSpeed = currentSpeed > 0.001f
                    && Vector3.Dot(target, currentOnGround) / currentSpeed < currentSpeed;
                float accel = (noInput || targetReducesSpeed)
                    ? _deceleration
                    : _acceleration;

                diff = Vector3.ClampMagnitude(diff, accel * deltaTime);
                Body.AddForce(diff, ForceMode.VelocityChange);
            }
            else
            {
                Vector3 horizontal = new Vector3(Body.Velocity.x, 0f, Body.Velocity.z);
                float effectiveMaxAirSpeed = _preserveTakeoffSpeed
                    ? Mathf.Max(_maxAirSpeed, _takeoffSpeed)
                    : _maxAirSpeed;

                if (_enableAirStrafe && target.sqrMagnitude > 0.01f)
                {
                    // Source/Quake-style air strafing:
                    // Only accelerate along wishdir when current projection onto it is below threshold.
                    // This lets players curve through the air with strafe + mouse turn.
                    Vector3 wishDir = target.normalized;
                    float currentSpeed = Vector3.Dot(horizontal, wishDir);
                    float addSpeed = _airStrafeMaxWishSpeed - currentSpeed;

                    if (addSpeed > 0f)
                    {
                        float accelSpeed = _airStrafeAccel * _airStrafeMaxWishSpeed * deltaTime;
                        if (accelSpeed > addSpeed)
                            accelSpeed = addSpeed;

                        Body.AddForce(wishDir * accelSpeed, ForceMode.VelocityChange);
                    }
                }
                else
                {
                    // Standard air control
                    Vector3 airForce = target * (_airControlForce * _airControl);

                    if (horizontal.magnitude < effectiveMaxAirSpeed)
                        Body.AddForce(airForce, ForceMode.Acceleration);
                }

                if (_airDrag > 0f)
                {
                    // When preserving takeoff speed, don't drag below the speed we launched at
                    if (_preserveTakeoffSpeed && horizontal.magnitude <= _takeoffSpeed)
                    {
                        // No drag — preserve momentum
                    }
                    else
                    {
                        Vector3 dragForce = -horizontal * _airDrag;
                        Body.AddForce(dragForce, ForceMode.Acceleration);
                    }
                }
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

        public void SaveState(ref ModuleStateWriter writer)
        {
            writer.Write(_wasGrounded);
            writer.Write(_takeoffSpeed);
        }

        public void RestoreState(ref ModuleStateReader reader)
        {
            _wasGrounded = reader.ReadBool();
            _takeoffSpeed = reader.ReadFloat();
        }
    }
}
