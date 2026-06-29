using System;
using UnityEngine;

namespace Rubickanov.Motor.Modules
{
    /// <summary>
    /// Sprint multiplier. Only active while grounded, moving forward, and not crouching.
    /// Supports optional ramp-up via AnimationCurve for gradual acceleration.
    /// </summary>
    [Serializable]
    public class SprintModule : MotorModuleBase, IStatefulModule
    {
        [UnityEngine.SerializeField] private float _sprintMultiplier = 1.6f;

        [Header("Ramp")]
        [SerializeField] private bool _useRamp;
        [SerializeField] private AnimationCurve _rampCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private float _rampUpDuration = 3f;
        [SerializeField] private float _rampDownDuration = 1f;

        public override int Priority => 5;

        private float _rampProgress;
        private bool _wasSprinting;

        /// <summary>Current sprint state.</summary>
        public bool IsSprinting => State.IsSprinting;

        /// <summary>Current ramp progress (0..1). Useful for UI or animation.</summary>
        public float RampProgress => _rampProgress;

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

            if (!_useRamp)
            {
                if (canSprint)
                    State.SpeedMultiplier *= _sprintMultiplier;
            }
            else
            {
                if (canSprint && _rampUpDuration > 0f)
                    _rampProgress = Mathf.Clamp01(_rampProgress + deltaTime / _rampUpDuration);
                else if (_rampDownDuration > 0f)
                    _rampProgress = Mathf.Clamp01(_rampProgress - deltaTime / _rampDownDuration);
                else
                    _rampProgress = 0f;

                if (_rampProgress > 0f)
                {
                    float t = _rampCurve.Evaluate(_rampProgress);
                    State.SpeedMultiplier *= Mathf.Lerp(1f, _sprintMultiplier, t);
                }
            }

            if (canSprint != _wasSprinting)
                SprintChanged?.Invoke(canSprint);

            _wasSprinting = canSprint;
        }

        public void SaveState(ref ModuleStateWriter writer)
        {
            writer.Write(_rampProgress);
        }

        public void RestoreState(ref ModuleStateReader reader)
        {
            _rampProgress = reader.ReadFloat();
        }
    }
}
