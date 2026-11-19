using Unity.Mathematics;

namespace Rubickanov.Kinematics
{
    /// <summary>A look direction as yaw and pitch in degrees. Yaw is positive to the right, pitch positive up.</summary>
    public readonly struct AimAngles
    {
        public readonly double Yaw;
        public readonly double Pitch;

        public AimAngles(double yaw, double pitch)
        {
            Yaw = yaw;
            Pitch = pitch;
        }

        /// <summary>From a direction in a space where Z is forward and Y is up; none for a zero direction.</summary>
        public static AimAngles FromLocal(double3 direction)
        {
            if (math.dot(direction, direction) < 1e-16)
            {
                return default;
            }

            direction = DetMath.Normalize(direction);
            double yaw = DetMath.Atan2(direction.x, direction.z) * DetMath.Rad2Deg;
            double pitch = DetMath.Asin(direction.y) * DetMath.Rad2Deg;
            return new AimAngles(yaw, pitch);
        }

        /// <summary>The direction these angles point, Z forward and Y up.</summary>
        public double3 Direction()
        {
            DetMath.SinCos(Yaw * DetMath.Deg2Rad, out double sy, out double cy);
            DetMath.SinCos(Pitch * DetMath.Deg2Rad, out double sp, out double cp);
            return new double3(sy * cp, sp, cy * cp);
        }

        /// <summary>Clamped to the limits. Past yawLimit the look holds at it rather than easing back.</summary>
        public AimAngles Limit(double yawLimit, double pitchUp, double pitchDown) =>
            Limit(yawLimit, yawLimit, pitchUp, pitchDown);

        /// <summary>Clamped to limits that may differ to the right and the left.</summary>
        public AimAngles Limit(double right, double left, double up, double down) =>
            new(DetMath.Clamp(Yaw, -left, right), DetMath.Clamp(Pitch, -down, up));
    }
}
