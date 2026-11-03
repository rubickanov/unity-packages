using UnityEngine;

namespace Rubickanov.Rig
{
    /// <summary>
    /// An offset of the pose that dies away (inertialization): taken as the old pose (moved on by its velocity to this
    /// frame) less the new one, with a rate that makes the offset pose move as the old one did, then laid over the new
    /// pose and faded to nothing. Along the offset it falls as <c>(1 − s)⁴(x₀ + (v₀T + 4x₀)s)</c>, which keeps the
    /// velocity, ends with no velocity or acceleration and never passes zero (the length is cut to −5x₀/v₀ when the
    /// offset closes fast); the rate across it rises from 0 and dies with it. Turns are rotation vectors on the left,
    /// in the bone's parent frame. Ported from SPLATDOWN's Sinew Animation/Inertialization.cs in float.
    /// </summary>
    public static class Inertia
    {
        /// <summary>
        /// The offset <paramref name="x"/> and its rate <paramref name="rate"/> from a turn <paramref name="last"/>
        /// spinning at <paramref name="lastSpin"/> (rad/s, in the parent frame) <paramref name="dt"/> s ago to the
        /// new turn <paramref name="target"/> spinning at <paramref name="targetSpin"/>.
        /// </summary>
        public static void BeginTurn(Quaternion last, Vector3 lastSpin, Quaternion target, Vector3 targetSpin,
            float dt, out Vector3 x, out Vector3 rate)
        {
            Quaternion ahead = Exp(dt * lastSpin) * last;
            x = Log(ahead * Quaternion.Inverse(target));
            // The rate that makes the spin of exp(x)·target the last one (see ApplyTurn).
            Vector3 rest = lastSpin - Exp(x) * targetSpin;
            rate = Unspin(x, rest);
        }

        /// <summary>The offset and rate of a position, as <see cref="BeginTurn"/>.</summary>
        public static void BeginPosition(Vector3 last, Vector3 lastVelocity, Vector3 target, Vector3 targetVelocity,
            float dt, out Vector3 x, out Vector3 rate)
        {
            x = last + dt * lastVelocity - target;
            rate = lastVelocity - targetVelocity;
        }

        /// <summary>A turn <paramref name="pose"/> with the offset as it stands at <paramref name="t"/> of
        /// <paramref name="length"/> s laid over it.</summary>
        public static Quaternion ApplyTurn(Vector3 x0, Vector3 v0, float length, float t, Quaternion pose)
        {
            Decay(x0, v0, length, t, out Vector3 x, out _);
            return Normalize(Exp(x) * pose);
        }

        /// <summary>A position with the offset laid over it, as <see cref="ApplyTurn"/>.</summary>
        public static Vector3 ApplyPosition(Vector3 x0, Vector3 v0, float length, float t, Vector3 pose)
        {
            Decay(x0, v0, length, t, out Vector3 x, out _);
            return pose + x;
        }

        /// <summary>
        /// The offset and its rate at time <paramref name="t"/> of <paramref name="length"/> s, from
        /// <paramref name="x0"/> and <paramref name="v0"/>: the part of the rate along the offset follows it, the
        /// rest rises from nothing and dies with it.
        /// </summary>
        public static void Decay(Vector3 x0, Vector3 v0, float length, float t, out Vector3 x, out Vector3 v)
        {
            x = Vector3.zero;
            v = Vector3.zero;
            float size = x0.magnitude;
            Vector3 across = v0;
            if (size > 1e-9f)
            {
                Vector3 n = x0 / size;
                float along = Vector3.Dot(v0, n);
                across = v0 - along * n;
                // Closing fast: shorter, so as not to pass zero.
                float end = along < 0f ? Mathf.Min(length, -5f * size / along) : length;
                Quartic(size, along, end, t, out float a, out float av);
                x += a * n;
                v += av * n;
            }

            Quartic(0f, 1f, length, t, out float b, out float bv);
            x += b * across;
            v += bv * across;
        }

        /// <summary>The spin of exp(x) as x changes at <paramref name="rate"/>: the left Jacobian of x times it.</summary>
        public static Vector3 Spin(Vector3 x, Vector3 rate)
        {
            float angle = x.magnitude;
            float a;
            float b;
            if (angle < 1e-3f)
            {
                a = 0.5f;
                b = 1f / 6f;
            }
            else
            {
                a = (1f - Mathf.Cos(angle)) / (angle * angle);
                b = (angle - Mathf.Sin(angle)) / (angle * angle * angle);
            }

            Vector3 cross = Vector3.Cross(x, rate);
            return rate + a * cross + b * Vector3.Cross(x, cross);
        }

        /// <summary>The rate of x that gives exp(x) <paramref name="spin"/>: <see cref="Spin"/> undone.</summary>
        public static Vector3 Unspin(Vector3 x, Vector3 spin)
        {
            float angle = x.magnitude;
            float c;
            if (angle < 1e-3f)
            {
                c = 1f / 12f;
            }
            else
            {
                c = 1f / (angle * angle) - Mathf.Cos(0.5f * angle) / (2f * angle * Mathf.Sin(0.5f * angle));
            }

            Vector3 cross = Vector3.Cross(x, spin);
            return spin - 0.5f * cross + c * Vector3.Cross(x, cross);
        }

        /// <summary>x(s) = (1 − s)⁴(x₀ + (v₀T + 4x₀)s), s = t / T, and its rate; 0 from T on.</summary>
        public static void Quartic(float x0, float v0, float length, float t, out float x, out float v)
        {
            if (!(length > 0f) || t >= length)
            {
                x = 0f;
                v = 0f;
                return;
            }

            float s = Mathf.Max(0f, t) / length;
            float k = v0 * length + 4f * x0;
            float u = 1f - s;
            float u3 = u * u * u;
            x = u3 * u * (x0 + k * s);
            v = u3 * (k - 4f * x0 - 5f * k * s) / length;
        }

        /// <summary>The turn of rotation vector <paramref name="r"/> (rad).</summary>
        public static Quaternion Exp(Vector3 r)
        {
            float angle = r.magnitude;
            if (angle < 1e-9f)
            {
                return Quaternion.identity;
            }

            float sin = Mathf.Sin(0.5f * angle);
            Vector3 axis = r / angle * sin;
            return new Quaternion(axis.x, axis.y, axis.z, Mathf.Cos(0.5f * angle));
        }

        /// <summary>The rotation vector of turn <paramref name="q"/>, the short way.</summary>
        public static Vector3 Log(Quaternion q)
        {
            if (q.w < 0f)
            {
                q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            }

            var v = new Vector3(q.x, q.y, q.z);
            float sin = v.magnitude;
            return sin < 1e-9f ? Vector3.zero : v / sin * (2f * Mathf.Atan2(sin, q.w));
        }

        /// <summary>The spin (rad/s) that turns <paramref name="from"/> into <paramref name="to"/> in
        /// <paramref name="dt"/> s (negative: <paramref name="from"/> comes after), on the left as the offsets
        /// are.</summary>
        public static Vector3 SpinBetween(Quaternion from, Quaternion to, float dt) =>
            dt != 0f ? Log(to * Quaternion.Inverse(from)) / dt : Vector3.zero;

        private static Quaternion Normalize(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 1e-9f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }
    }
}
