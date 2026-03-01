using System;
using UnityEngine;

namespace Rubickanov.Motor.Modules
{
    /// <summary>
    /// Toggle crouch with smooth height transition and ceiling detection.
    /// </summary>
    [Serializable]
    public class CrouchModule : MotorModuleBase, IStatefulModule
    {
        [SerializeField] private float _standHeight = 2f;
        [SerializeField] private float _crouchHeight = 1.2f;
        [SerializeField] private float _crouchSpeedMultiplier = 0.45f;
        [SerializeField] private float _crouchTransitionSpeed = 10f;
        [SerializeField] private float _ceilingCheckRadius = 0.25f;

        public override int Priority => 15;

        private float _currentHeight;
        private bool _isCrouching;
        private bool _wasCrouching;

        /// <summary>
        /// Camera height offset caused by crouching.
        /// Game code can read this to adjust camera position.
        /// </summary>
        public float CameraHeightOffset { get; private set; }

        /// <summary>Fired on crouch state change.</summary>
        public event Action<bool>? CrouchChanged;

        protected override void OnInitialize()
        {
            _currentHeight = _standHeight;
        }

        public override void Simulate(float deltaTime)
        {
            // Toggle
            if (State.CrouchPressed)
                _isCrouching = !_isCrouching;

            // Can't stand up — ceiling above
            if (!_isCrouching && !CanStand())
                _isCrouching = true;

            State.IsCrouching = _isCrouching;

            if (_isCrouching)
                State.SpeedMultiplier *= _crouchSpeedMultiplier;

            // Height transition (deterministic — must run in Simulate, not VisualUpdate)
            float target = _isCrouching ? _crouchHeight : _standHeight;
            _currentHeight = Mathf.MoveTowards(_currentHeight, target, _crouchTransitionSpeed * deltaTime);
            Body.SetCapsuleHeight(_currentHeight);

            if (_isCrouching != _wasCrouching)
                CrouchChanged?.Invoke(_isCrouching);
            _wasCrouching = _isCrouching;
        }

        public override void VisualUpdate(float deltaTime)
        {
            CameraHeightOffset = _currentHeight - _standHeight;
        }

        private bool CanStand()
        {
            float checkDist = _standHeight - _crouchHeight;
            return !Body.SphereCast(
                Body.Position + Vector3.up * _crouchHeight,
                _ceilingCheckRadius, Vector3.up, checkDist, out _);
        }

        public void SaveState(ref ModuleStateWriter writer)
        {
            writer.Write(_currentHeight);
            writer.Write(_isCrouching);
        }

        public void RestoreState(ref ModuleStateReader reader)
        {
            _currentHeight = reader.ReadFloat();
            _isCrouching = reader.ReadBool();
        }
    }
}
