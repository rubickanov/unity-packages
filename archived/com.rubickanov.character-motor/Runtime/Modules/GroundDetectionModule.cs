using System;
using UnityEngine;

namespace Rubickanov.Motor.Modules
{
    /// <summary>
    /// Detects ground beneath the controller using SphereCast.
    /// Runs first (lowest priority) so other modules have accurate ground info.
    /// </summary>
    [Serializable]
    public class GroundDetectionModule : MotorModuleBase
    {
        [SerializeField] private float _maxSlopeAngle = 46f;
        [SerializeField] private float _groundCheckRadius = 0.25f;
        [SerializeField] private float _groundCheckDistance = 0.12f;

        public override int Priority => -100;

        private bool _wasGrounded;

        /// <summary>Fired when grounded state changes.</summary>
        public event Action<bool>? GroundedChanged;

        public override void Simulate(float deltaTime)
        {
            float radius = _groundCheckRadius;
            float distance = _groundCheckDistance;
            var origin = Body.Position + Vector3.up * (radius + 0.05f);

            if (Body.SphereCast(origin, radius, Vector3.down, distance, out var hit))
            {
                float angle = Vector3.Angle(Vector3.up, hit.normal);

                State.GroundNormal = hit.normal;
                State.GroundAngle = angle;
                State.IsGrounded = angle <= _maxSlopeAngle;

                var attachedRb = hit.collider.attachedRigidbody;
                if (attachedRb != null && State.IsGrounded)
                    State.GroundVelocity = attachedRb.GetPointVelocity(hit.point);
                else
                    State.GroundVelocity = Vector3.zero;
            }
            else
            {
                State.IsGrounded = false;
                State.GroundNormal = Vector3.up;
                State.GroundAngle = 0f;
                State.GroundVelocity = Vector3.zero;
            }

            State.IsInAir = !State.IsGrounded;

            if (_wasGrounded != State.IsGrounded)
                GroundedChanged?.Invoke(State.IsGrounded);
            _wasGrounded = State.IsGrounded;
        }
    }
}
