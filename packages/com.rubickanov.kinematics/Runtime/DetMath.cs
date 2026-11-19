using Unity.Mathematics;

namespace Rubickanov.Kinematics
{
    /// <summary>
    /// Scalar and vector maths in double built only from +, −, ×, ÷ and the square root, which IEEE rounds the same
    /// everywhere: Mono and Burst in its deterministic mode give the same bits, on any machine. The platform's sine,
    /// arctangent and exponent are left out, as they may round apart. Angles are radians unless a name says degrees.
    /// </summary>
    public static class DetMath
    {
        public const double Pi = 3.14159265358979323846;
        public const double HalfPi = 1.57079632679489661923;
        public const double Rad2Deg = 57.2957795130823208768;
        public const double Deg2Rad = 0.0174532925199432957692;

        // π/2 and ln 2 in two parts, the first with trailing zero bits, so k·part is exact for the k met here.
        private const double HalfPiHigh = 1.57079632673412561417e+00;
        private const double HalfPiLow = 6.07710050650619224932e-11;
        private const double Ln2High = 6.93147180369123816490e-01;
        private const double Ln2Low = 1.90821492927058770002e-10;

        public static double Sqrt(double x) => math.sqrt(x);

        public static double Clamp(double x, double min, double max) => x < min ? min : x > max ? max : x;

        public static double Clamp01(double x) => Clamp(x, 0.0, 1.0);

        public static void SinCos(double x, out double sin, out double cos)
        {
            double k = math.floor(x * (2.0 / Pi) + 0.5);
            double r = x - k * HalfPiHigh - k * HalfPiLow;
            double r2 = r * r;
            // Taylor series on |r| ≤ π/4, to well past double precision.
            double s = r + r * r2 * (-1.0 / 6 + r2 * (1.0 / 120 + r2 * (-1.0 / 5040 + r2 * (1.0 / 362880
                + r2 * (-1.0 / 39916800 + r2 * (1.0 / 6227020800 + r2 * (-1.0 / 1307674368000
                + r2 * (1.0 / 355687428096000))))))));
            double c = 1.0 + r2 * (-0.5 + r2 * (1.0 / 24 + r2 * (-1.0 / 720 + r2 * (1.0 / 40320
                + r2 * (-1.0 / 3628800 + r2 * (1.0 / 479001600 + r2 * (-1.0 / 87178291200
                + r2 * (1.0 / 20922789888000 + r2 * (-1.0 / 6402373705728000)))))))));
            long quadrant = (long)k & 3;
            switch (quadrant)
            {
                case 0:
                    sin = s;
                    cos = c;
                    break;
                case 1:
                    sin = c;
                    cos = -s;
                    break;
                case 2:
                    sin = -s;
                    cos = -c;
                    break;
                default:
                    sin = -c;
                    cos = s;
                    break;
            }
        }

        public static double Sin(double x)
        {
            SinCos(x, out double s, out _);
            return s;
        }

        public static double Cos(double x)
        {
            SinCos(x, out _, out double c);
            return c;
        }

        public static double Atan(double x)
        {
            double a = math.abs(x);
            bool flip = a > 1.0;
            if (flip)
            {
                a = 1.0 / a;
            }

            // Three half-angle steps, atan t = 2 atan(t / (1 + √(1 + t²))), leave t ≤ 0.0985 for the series.
            for (var i = 0; i < 3; i++)
            {
                a /= 1.0 + math.sqrt(1.0 + a * a);
            }

            double t2 = a * a;
            double series = a * (1.0 + t2 * (-1.0 / 3 + t2 * (1.0 / 5 + t2 * (-1.0 / 7 + t2 * (1.0 / 9
                + t2 * (-1.0 / 11 + t2 * (1.0 / 13 + t2 * (-1.0 / 15 + t2 * (1.0 / 17 + t2 * (-1.0 / 19
                + t2 * (1.0 / 21)))))))))));
            double angle = 8.0 * series;
            if (flip)
            {
                angle = HalfPi - angle;
            }

            return x < 0.0 ? -angle : angle;
        }

        public static double Atan2(double y, double x)
        {
            if (x > 0.0)
            {
                return Atan(y / x);
            }

            if (x < 0.0)
            {
                return y >= 0.0 ? Atan(y / x) + Pi : Atan(y / x) - Pi;
            }

            return y > 0.0 ? HalfPi : y < 0.0 ? -HalfPi : 0.0;
        }

        public static double Asin(double x)
        {
            x = Clamp(x, -1.0, 1.0);
            return Atan2(x, math.sqrt((1.0 - x) * (1.0 + x)));
        }

        public static double Acos(double x)
        {
            x = Clamp(x, -1.0, 1.0);
            return Atan2(math.sqrt((1.0 - x) * (1.0 + x)), x);
        }

        public static double Exp(double x)
        {
            if (x < -708.0)
            {
                return 0.0;
            }

            if (x > 709.0)
            {
                return double.PositiveInfinity;
            }

            double k = math.floor(x * (1.0 / 0.693147180559945309417) + 0.5);
            double r = x - k * Ln2High - k * Ln2Low;
            // Taylor series on |r| ≤ ln 2 / 2.
            double p = 1.0 + r * (1.0 + r * (1.0 / 2 + r * (1.0 / 6 + r * (1.0 / 24 + r * (1.0 / 120
                + r * (1.0 / 720 + r * (1.0 / 5040 + r * (1.0 / 40320 + r * (1.0 / 362880 + r * (1.0 / 3628800
                + r * (1.0 / 39916800 + r * (1.0 / 479001600 + r * (1.0 / 6227020800 + r * (1.0 / 87178291200
                + r * (1.0 / 1307674368000)))))))))))))));
            return p * math.asdouble((long)(k + 1023.0) << 52);
        }

        /// <summary>The shortest turn from <paramref name="current"/> to <paramref name="target"/>, degrees in
        /// [−180, 180].</summary>
        public static double DeltaAngle(double current, double target)
        {
            double delta = Repeat(target - current, 360.0);
            return delta > 180.0 ? delta - 360.0 : delta;
        }

        /// <summary><paramref name="t"/> wrapped into [0, <paramref name="length"/>].</summary>
        public static double Repeat(double t, double length) => Clamp(t - math.floor(t / length) * length, 0.0, length);

        /// <summary>From <paramref name="a"/> toward <paramref name="b"/> the short way round, degrees.</summary>
        public static double LerpAngle(double a, double b, double t) => a + DeltaAngle(a, b) * Clamp01(t);

        // ----- Vectors ------------------------------------------------------------------------------------------

        public static double Length(double3 v) => math.sqrt(math.dot(v, v));

        public static double Distance(double3 a, double3 b) => Length(a - b);

        /// <summary><paramref name="v"/> scaled to length 1; zero when it is (nearly) zero.</summary>
        public static double3 Normalize(double3 v)
        {
            double length = Length(v);
            return length > 1e-300 ? v / length : double3.zero;
        }

        /// <summary>What is left of <paramref name="v"/> across <paramref name="normal"/> (any length).</summary>
        public static double3 ProjectOnPlane(double3 v, double3 normal)
        {
            double n2 = math.dot(normal, normal);
            return n2 > 1e-300 ? v - normal * (math.dot(v, normal) / n2) : v;
        }

        /// <summary>The angle between two vectors, radians in [0, π]; zero when either is zero.</summary>
        public static double Angle(double3 a, double3 b) => Atan2(Length(math.cross(a, b)), math.dot(a, b));

        /// <summary>The angle from <paramref name="from"/> to <paramref name="to"/>, signed by the turn about
        /// <paramref name="axis"/>, radians in [−π, π].</summary>
        public static double SignedAngle(double3 from, double3 to, double3 axis)
        {
            double3 cross = math.cross(from, to);
            double angle = Atan2(Length(cross), math.dot(from, to));
            return math.dot(axis, cross) < 0.0 ? -angle : angle;
        }
    }
}
