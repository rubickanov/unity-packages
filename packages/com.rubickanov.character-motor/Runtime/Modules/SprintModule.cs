using System;
using UnityEngine;

namespace Rubickanov.Motor.Modules
{
    /// <summary>
    /// Sprint multiplier. Only active while grounded, moving forward, and not crouching.
    /// </summary>
    [Serializable]
    public class SprintModule : MotorModuleBase
    {
        [UnityEngine.SerializeField] private float _sprintMultiplier = 1.6f;

        public override int Priority => 5;

        private bool _wasSprinting;

        /// <summary>Current sprint state.</summary>
        public bool IsSprinting => State.IsSprinting;

        /// <summary>Fired when sprint state changes.</summary>
        public event Action<bool>? SprintChanged;

        public override void Simulate(float deltaTime)
        {
            bool hasForwardInput = State.MoveInput.sqrMagnitude > 0.01f
                && Vector2.Dot(State.MoveInput.normalized, Vector2.up) > 0.5f;

            bool canSprint = State.SprintHeld
                          && State.IsGrounded
                          && hasForwardInput
                          && !State.IsCrouching;

            State.IsSprinting = canSprint;

            if (canSprint)
                State.SpeedMultiplier *= _sprintMultiplier;

            if (canSprint != _wasSprinting)
                SprintChanged?.Invoke(canSprint);

            _wasSprinting = canSprint;
        }
    }
}
