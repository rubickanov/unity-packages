using UnityEngine;

namespace Rubickanov.Camera
{
    /// <summary>
    /// No-op camera service for server and headless builds.
    /// </summary>
    public class NullCameraService : ICameraService
    {
        public UnityEngine.Camera Camera => null!;
        public void SetFollowTarget(Transform target) { }
        public void ClearFollowTarget() { }
        public void SetAimOffset(Vector3 offset) { }
        public void Shake(Vector3 direction, float force) { }
    }
}
