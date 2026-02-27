using UnityEngine;

namespace Rubickanov.Camera
{
    /// <summary>
    /// Interface for camera follow, aim offset, and screen shake.
    /// </summary>
    public interface ICameraService
    {
        UnityEngine.Camera Camera { get; }

        void SetFollowTarget(Transform target);
        void ClearFollowTarget();
        void SetAimOffset(Vector3 offset);
        void Shake(Vector3 direction, float force);
    }
}
