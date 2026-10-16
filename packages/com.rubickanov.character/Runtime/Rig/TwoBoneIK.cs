using UnityEngine;

namespace Rubickanov.Rig
{
    /// <summary>Analytic two-bone solver for a leg or an arm, as a post-process on the animated pose.</summary>
    public static class TwoBoneIK
    {
        /// <summary>
        /// Bends upper and lower so that end reaches target, with the middle joint pointing toward hint.
        /// End keeps its world rotation. Softness is the fraction of the full reach over which the chain
        /// eases into straightening, so the knee never snaps straight.
        /// </summary>
        public static void Solve(Transform upper, Transform lower, Transform end,
            Vector3 target, Vector3 hint, float weight, float softness = 0.02f)
        {
            if (weight <= 0f)
            {
                return;
            }

            Quaternion upperStart = upper.rotation;
            Quaternion lowerStart = lower.rotation;
            Quaternion endStart = end.rotation;

            Vector3 a = upper.position;
            Vector3 b = lower.position;
            Vector3 c = end.position;
            float upperLength = Vector3.Distance(a, b);
            float lowerLength = Vector3.Distance(b, c);
            if (upperLength < 1e-5f || lowerLength < 1e-5f)
            {
                return;
            }

            Vector3 toTarget = target - a;
            float distance = toTarget.magnitude;
            if (distance < 1e-5f)
            {
                return;
            }

            distance = SoftDistance(distance, upperLength + lowerLength, softness);
            distance = Mathf.Max(distance, Mathf.Abs(upperLength - lowerLength) + 1e-4f);
            target = a + toTarget / toTarget.magnitude * distance;

            // 1. Open or close the middle joint until the chain spans the distance.
            float current = Vector3.Angle(a - b, c - b);
            float wanted = Mathf.Acos(Mathf.Clamp((upperLength * upperLength + lowerLength * lowerLength
                - distance * distance) / (2f * upperLength * lowerLength), -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 axis = Vector3.Cross(c - b, a - b);
            if (axis.sqrMagnitude < 1e-8f)
            {
                // Straight chain: any axis across it works, step 3 turns the bend toward the hint.
                axis = Vector3.Cross(c - a, hint - a);
                if (axis.sqrMagnitude < 1e-8f)
                {
                    axis = Vector3.Cross(c - a, upper.rotation * Vector3.forward);
                }
            }

            lower.rotation = Quaternion.AngleAxis(current - wanted, axis.normalized) * lower.rotation;

            // 2. Swing the whole chain onto the target.
            upper.rotation = Quaternion.FromToRotation(end.position - a, target - a) * upper.rotation;

            // 3. Twist it about the reach line so the middle joint points toward the hint.
            Vector3 reach = (target - a).normalized;
            Vector3 bend = Vector3.ProjectOnPlane(lower.position - a, reach);
            Vector3 wantedBend = Vector3.ProjectOnPlane(hint - a, reach);
            if (bend.sqrMagnitude > 1e-8f && wantedBend.sqrMagnitude > 1e-8f)
            {
                float twist = Vector3.SignedAngle(bend, wantedBend, reach);
                upper.rotation = Quaternion.AngleAxis(twist, reach) * upper.rotation;
            }

            if (weight < 1f)
            {
                upper.rotation = Quaternion.Slerp(upperStart, upper.rotation, weight);
                lower.rotation = Quaternion.Slerp(lowerStart, lower.rotation, weight);
            }

            end.rotation = endStart;
        }

        /// <summary>
        /// Past the soft zone the reachable distance approaches full length asymptotically,
        /// so the joint straightens smoothly instead of popping at full extension.
        /// </summary>
        public static float SoftDistance(float distance, float length, float softness)
        {
            float soft = length * softness;
            float hard = length - soft;
            if (soft <= 0f || distance <= hard)
            {
                return Mathf.Min(distance, length);
            }

            return hard + soft * (1f - Mathf.Exp(-(distance - hard) / soft));
        }
    }
}
