using System;
using Unity.Cinemachine;
using UnityEngine;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Rubickanov.Camera
{
    /// <summary>
    /// Cinemachine v3 camera service with proxy-based follow, smooth aim offset, and impulse shake.
    /// </summary>
    public class CinemachineCameraService : ICameraService, ILateTickable, IDisposable
    {
        private readonly CinemachineCamera _gameplayCamera;
        private readonly CameraConfig _config;
        private readonly Transform _proxy;
        private readonly CinemachineImpulseSource _impulseSource;

        private UnityEngine.Camera? _outputCamera;
        public UnityEngine.Camera Camera
        {
            get
            {
                if (_outputCamera == null)
                {
                    _outputCamera = UnityEngine.Camera.main;
                    if (_outputCamera == null)
                        throw new InvalidOperationException("No main camera found. Ensure a Camera with MainCamera tag exists in the scene.");
                }
                return _outputCamera;
            }
        }

        private Transform? _target;
        private Vector3 _targetAimOffset;
        private Vector3 _smoothAimOffset;

        public CinemachineCameraService(CinemachineCamera gameplayCamera, CameraConfig config)
        {
            _gameplayCamera = gameplayCamera;
            _config = config;

            var proxyGo = new GameObject("[CameraProxy]");
            Object.DontDestroyOnLoad(proxyGo);
            _proxy = proxyGo.transform;

            _impulseSource = proxyGo.AddComponent<CinemachineImpulseSource>();
            _impulseSource.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Recoil;
            _impulseSource.ImpulseDefinition.ImpulseDuration = 0.12f;
            _impulseSource.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        }

        public void SetFollowTarget(Transform target)
        {
            _target = target;
            _targetAimOffset = Vector3.zero;
            _smoothAimOffset = Vector3.zero;
            _proxy.position = target.position;
            _gameplayCamera.Target.TrackingTarget = _proxy;
        }

        public void ClearFollowTarget()
        {
            _target = null;
            _targetAimOffset = Vector3.zero;
            _smoothAimOffset = Vector3.zero;
            _gameplayCamera.Target.TrackingTarget = null;
        }

        public void SetAimOffset(Vector3 offset)
        {
            _targetAimOffset = offset;
        }

        public void Shake(Vector3 direction, float force)
        {
            _impulseSource.GenerateImpulseWithVelocity(direction * force);
        }

        void ILateTickable.LateTick()
        {
            if (_target == null) return;
            _smoothAimOffset = Vector3.Lerp(_smoothAimOffset, _targetAimOffset, _config.AimSmoothSpeed * Time.deltaTime);
            _proxy.position = _target.position + _smoothAimOffset;
        }

        public void Dispose()
        {
            _target = null;
            if (_proxy != null)
                Object.Destroy(_proxy.gameObject);
        }
    }
}
