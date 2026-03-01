using System;
using UnityEngine;

namespace Rubickanov.Motor.Modules
{
    /// <summary>
    /// How movement input is interpreted.
    /// </summary>
    public enum MovementOrientation
    {
        /// <summary>WASD maps to world axes (TopDown).</summary>
        World,

        /// <summary>Relative to an orientation source transform (FPS/TPS).</summary>
        Transform
    }

    /// <summary>
    /// Translates move input into desired velocity.
    /// Supports world-axis and transform-relative orientation modes.
    /// </summary>
    [Serializable]
    public class MovementModule : MotorModuleBase
    {
        [SerializeField] private float _walkSpeed = 6f;
        [SerializeField] private MovementOrientation _orientation = MovementOrientation.Transform;
        [SerializeField] private bool _allowVerticalMovement;

        public override int Priority => 0;

        private Transform? _orientationSource;

        /// <summary>
        /// When true, uses raw forward/right vectors preserving the Y component.
        /// Enable for swimming, flying, or other 3D movement modes.
        /// </summary>
        public bool AllowVerticalMovement
        {
            get => _allowVerticalMovement;
            set => _allowVerticalMovement = value;
        }

        /// <summary>How movement input is interpreted.</summary>
        public MovementOrientation Orientation
        {
            get => _orientation;
            set => _orientation = value;
        }

        /// <summary>
        /// Set the transform used for direction when <see cref="Orientation"/> is
        /// <see cref="MovementOrientation.Transform"/>. Falls back to motor's own transform.
        /// </summary>
        public void SetOrientationSource(Transform source)
        {
            _orientationSource = source;
        }

        public override void Simulate(float deltaTime)
        {
            var input = State.MoveInput;
            if (input.sqrMagnitude < 0.001f)
            {
                State.DesiredVelocity = Vector3.zero;
                return;
            }

            Vector3 forward;
            Vector3 right;

            if (_orientation == MovementOrientation.World)
            {
                forward = Vector3.forward;
                right = Vector3.right;
            }
            else
            {
                var source = _orientationSource != null ? _orientationSource : Body.Transform;
                if (_allowVerticalMovement)
                {
                    forward = source.forward;
                    right = source.right;
                }
                else
                {
                    forward = Vector3.ProjectOnPlane(source.forward, Vector3.up).normalized;
                    right = Vector3.ProjectOnPlane(source.right, Vector3.up).normalized;
                }
            }

            Vector3 direction = forward * input.y + right * input.x;
            if (direction.sqrMagnitude > 1f)
                direction.Normalize();

            State.DesiredVelocity = direction * _walkSpeed;
        }
    }
}
