using UnityEngine;

namespace Rubickanov.Rig
{
    /// <summary>A look direction as yaw and pitch in degrees. Yaw is positive to the right, pitch positive up.</summary>
    public readonly struct AimAngles
    {
        public readonly float Yaw;
        public readonly float Pitch;

        public AimAngles(float yaw, float pitch)
        {
            Yaw = yaw;
            Pitch = pitch;
        }

        /// <summary>From a direction in a space where Z is forward and Y is up.</summary>
        public static AimAngles FromLocal(Vector3 direction)
        {
            if (direction.sqrMagnitude < 1e-8f)
            {
                return default;
            }

            direction.Normalize();
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            return new AimAngles(yaw, pitch);
        }

        /// <summary>Clamped to the limits. Past yawLimit the look holds at it rather than easing back.</summary>
        public AimAngles Limit(float yawLimit, float pitchUp, float pitchDown) =>
            new(Mathf.Clamp(Yaw, -yawLimit, yawLimit), Mathf.Clamp(Pitch, -pitchDown, pitchUp));
    }
}
