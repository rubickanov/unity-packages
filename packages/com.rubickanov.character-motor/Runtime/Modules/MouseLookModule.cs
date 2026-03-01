using System;
using UnityEngine;

namespace Rubickanov.Motor.Modules
{
    /// <summary>
    /// Mouse look with vertical clamp. Rotates the motor body horizontally
    /// and applies vertical pitch to a camera transform.
    /// Runs in VisualUpdate (not part of deterministic simulation) unless
    /// <see cref="_deterministicYaw"/> is enabled.
    /// </summary>
    [Serializable]
    public class MouseLookModule : MotorModuleBase, IStatefulModule
    {
        [SerializeField] private float _sensitivity = 2f;
        [SerializeField] private float _verticalClamp = 89f;
        [SerializeField] private bool _deterministicYaw;

        public override int Priority => -50;

        private Func<Vector2>? _lookInputProvider;
        private float _pitch;
        private Transform? _cameraTransform;

        /// <summary>Current vertical pitch in degrees.</summary>
        public float Pitch => _pitch;

        /// <summary>
        /// Set the look input provider. Required for the module to function.
        /// </summary>
        public void SetLookInputProvider(Func<Vector2> provider)
        {
            _lookInputProvider = provider;
        }

        /// <summary>
        /// Provide the camera transform so the module can rotate it vertically.
        /// If not set, only horizontal rotation is applied to the motor body.
        /// </summary>
        public void SetCameraTransform(Transform cameraTransform)
        {
            _cameraTransform = cameraTransform;
        }

        public override void Simulate(float deltaTime)
        {
            if (!_deterministicYaw) return;
            if (State.LookInput.x == 0f) return;

            Body.Rotate(Vector3.up, State.LookInput.x * _sensitivity, Space.World);
        }

        public override void VisualUpdate(float deltaTime)
        {
            if (_lookInputProvider == null) return;

            var look = _lookInputProvider();
            if (look.sqrMagnitude < 0.0001f) return;

            // Horizontal — rotate the body (only when not handled deterministically)
            if (!_deterministicYaw)
            {
                float yaw = look.x * _sensitivity;
                Body.Rotate(Vector3.up, yaw, Space.World);
            }

            // Vertical — pitch
            _pitch -= look.y * _sensitivity;
            _pitch = Mathf.Clamp(_pitch, -_verticalClamp, _verticalClamp);

            if (_cameraTransform != null)
                _cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        public void SaveState(ref ModuleStateWriter writer)
        {
            writer.Write(_pitch);
        }

        public void RestoreState(ref ModuleStateReader reader)
        {
            _pitch = reader.ReadFloat();
        }
    }
}
