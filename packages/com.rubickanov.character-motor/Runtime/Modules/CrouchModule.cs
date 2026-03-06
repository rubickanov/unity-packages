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
        private Rigidbody _ownRb = default!;
        private readonly RaycastHit[] _ceilingHits = new RaycastHit[4];

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
            _ownRb = Body.Transform.GetComponentInParent<Rigidbody>();
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
            Vector3 origin = Body.Position + Vector3.up * _crouchHeight;

            int count = Physics.SphereCastNonAlloc(
                origin, _ceilingCheckRadius, Vector3.up, _ceilingHits,
                checkDist, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (_ceilingHits[i].collider.attachedRigidbody != _ownRb)
                    return false;
            }

            return true;
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
