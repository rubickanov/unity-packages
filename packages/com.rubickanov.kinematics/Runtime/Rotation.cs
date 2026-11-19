using Unity.Mathematics;

namespace Rubickanov.Kinematics
{
    /// <summary>
    /// Unit quaternions in double, as (x, y, z, w) the way Unity and Unity.Mathematics write them, on
    /// <see cref="DetMath"/>'s exact operations. Axes as in the solver: X right, Y up, Z forward; a positive turn about
    /// an axis is the one Unity's AngleAxis makes.
    /// </summary>
    public static class Rotation
    {
        public static double4 Identity => new(0.0, 0.0, 0.0, 1.0);

        public static double4 From(quaternion q) => new(q.value.x, q.value.y, q.value.z, q.value.w);

        public static quaternion ToQuaternion(double4 q) => new((float)q.x, (float)q.y, (float)q.z, (float)q.w);

        public static double4 Mul(double4 a, double4 b) => new(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
            a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        /// <summary>The inverse of a unit quaternion.</summary>
        public static double4 Inverse(double4 q) => new(-q.x, -q.y, -q.z, q.w);

        public static double3 Rotate(double4 q, double3 v)
        {
            double3 u = q.xyz;
            double3 t = 2.0 * math.cross(u, v);
            return v + q.w * t + math.cross(u, t);
        }

        public static double4 Normalize(double4 q)
        {
            double length = math.sqrt(math.dot(q, q));
            return length > 1e-300 ? q / length : Identity;
        }

        /// <summary>A turn of <paramref name="radians"/> about <paramref name="axis"/> (any length; none when
        /// zero).</summary>
        public static double4 AxisAngle(double3 axis, double radians)
        {
            double3 n = DetMath.Normalize(axis);
            if (math.all(n == 0.0))
            {
                return Identity;
            }

            DetMath.SinCos(0.5 * radians, out double s, out double c);
            return new double4(n * s, c);
        }

        /// <summary>The smallest turn that carries direction <paramref name="from"/> onto <paramref name="to"/>.</summary>
        public static double4 FromTo(double3 from, double3 to)
        {
            double3 f = DetMath.Normalize(from);
            double3 t = DetMath.Normalize(to);
            if (math.all(f == 0.0) || math.all(t == 0.0))
            {
                return Identity;
            }

            double d = math.dot(f, t);
            if (d < -1.0 + 1e-12)
            {
                // Opposite: half a turn about any axis across them.
                double3 axis = math.cross(new double3(1.0, 0.0, 0.0), f);
                if (math.dot(axis, axis) < 1e-12)
                {
                    axis = math.cross(new double3(0.0, 1.0, 0.0), f);
                }

                return new double4(DetMath.Normalize(axis), 0.0);
            }

            return Normalize(new double4(math.cross(f, t), 1.0 + d));
        }

        /// <summary>The short way from <paramref name="a"/> (t = 0) to <paramref name="b"/> (t = 1), t clamped.</summary>
        public static double4 Slerp(double4 a, double4 b, double t)
        {
            t = DetMath.Clamp01(t);
            double d = math.dot(a, b);
            if (d < 0.0)
            {
                b = -b;
                d = -d;
            }

            if (d > 0.9999995)
            {
                return Normalize(a + (b - a) * t);
            }

            double angle = DetMath.Acos(d);
            double sin = DetMath.Sin(angle);
            double wa = DetMath.Sin((1.0 - t) * angle) / sin;
            double wb = DetMath.Sin(t * angle) / sin;
            return Normalize(a * wa + b * wb);
        }

        /// <summary>The angle of the turn between two rotations, radians in [0, π].</summary>
        public static double Angle(double4 a, double4 b)
        {
            double d = math.abs(math.dot(a, b));
            return 2.0 * DetMath.Acos(d > 1.0 ? 1.0 : d);
        }
    }
}
