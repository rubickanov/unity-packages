using Unity.Mathematics;

namespace Rubickanov.Kinematics
{
    /// <summary>
    /// A leg or an arm as three joints in the world: the upper bone turns about <see cref="UpperPosition"/> and
    /// carries the lower bone and the end, the lower bone turns about <see cref="LowerPosition"/> and carries the end.
    /// Rotations are the bones' world rotations.
    /// </summary>
    public struct TwoBoneChain
    {
        public double3 UpperPosition;
        public double3 LowerPosition;
        public double3 EndPosition;
        public double4 UpperRotation;
        public double4 LowerRotation;
        public double4 EndRotation;

        public TwoBoneChain(double3 upper, double3 lower, double3 end, double4 upperRotation, double4 lowerRotation,
            double4 endRotation)
        {
            UpperPosition = upper;
            LowerPosition = lower;
            EndPosition = end;
            UpperRotation = upperRotation;
            LowerRotation = lowerRotation;
            EndRotation = endRotation;
        }
    }

    /// <summary>Analytic two-bone solver for a leg or an arm, as a post-process on the animated pose.</summary>
    public static class TwoBoneIK
    {
        /// <summary>The softness <see cref="Solve"/> takes unless told otherwise.</summary>
        public const double Softness = 0.02;

        /// <summary>
        /// Bends the upper and lower bones so that the end reaches <paramref name="target"/>, the middle joint
        /// pointing toward <paramref name="hint"/>. The end keeps its world rotation. Softness is the fraction of the
        /// full reach over which the chain eases into straightening, so the knee never snaps straight.
        /// </summary>
        public static TwoBoneChain Solve(in TwoBoneChain chain, double3 target, double3 hint, double weight,
            double softness = Softness)
        {
            if (!(weight > 0.0))
            {
                return chain;
            }

            double3 a = chain.UpperPosition;
            double3 b = chain.LowerPosition;
            double3 c = chain.EndPosition;
            double upperLength = DetMath.Distance(a, b);
            double lowerLength = DetMath.Distance(b, c);
            if (upperLength < 1e-5 || lowerLength < 1e-5)
            {
                return chain;
            }

            double3 toTarget = target - a;
            double reachLength = DetMath.Length(toTarget);
            if (reachLength < 1e-5)
            {
                return chain;
            }

            double distance = SoftDistance(reachLength, upperLength + lowerLength, softness);
            distance = math.max(distance, math.abs(upperLength - lowerLength) + 1e-4);
            target = a + toTarget / reachLength * distance;

            // 1. Open or close the middle joint until the chain spans the distance.
            double current = DetMath.Angle(a - b, c - b);
            double wanted = DetMath.Acos((upperLength * upperLength + lowerLength * lowerLength - distance * distance)
                / (2.0 * upperLength * lowerLength));
            double3 axis = math.cross(c - b, a - b);
            if (math.dot(axis, axis) < 1e-8)
            {
                // Straight chain: any axis across it works, step 3 turns the bend toward the hint.
                axis = math.cross(c - a, hint - a);
                if (math.dot(axis, axis) < 1e-8)
                {
                    axis = math.cross(c - a, Rotation.Rotate(chain.UpperRotation, new double3(0.0, 0.0, 1.0)));
                }
            }

            double4 open = Rotation.AxisAngle(axis, current - wanted);
            double4 lower = Rotation.Mul(open, chain.LowerRotation);
            c = b + Rotation.Rotate(open, c - b);

            // 2. Swing the whole chain onto the target.
            double4 swing = Rotation.FromTo(c - a, target - a);
            double4 upper = Rotation.Mul(swing, chain.UpperRotation);
            lower = Rotation.Mul(swing, lower);
            b = a + Rotation.Rotate(swing, b - a);
            c = a + Rotation.Rotate(swing, c - a);

            // 3. Twist it about the reach line so the middle joint points toward the hint.
            double3 reach = DetMath.Normalize(target - a);
            double3 bend = DetMath.ProjectOnPlane(b - a, reach);
            double3 wantedBend = DetMath.ProjectOnPlane(hint - a, reach);
            if (math.dot(bend, bend) > 1e-8 && math.dot(wantedBend, wantedBend) > 1e-8)
            {
                double4 twist = Rotation.AxisAngle(reach, DetMath.SignedAngle(bend, wantedBend, reach));
                upper = Rotation.Mul(twist, upper);
                lower = Rotation.Mul(twist, lower);
                b = a + Rotation.Rotate(twist, b - a);
                c = a + Rotation.Rotate(twist, c - a);
            }

            if (weight < 1.0)
            {
                upper = Rotation.Slerp(chain.UpperRotation, upper, weight);
                lower = Rotation.Slerp(chain.LowerRotation, lower, weight);
                b = a + Rotation.Rotate(Rotation.Mul(upper, Rotation.Inverse(chain.UpperRotation)),
                    chain.LowerPosition - a);
                c = b + Rotation.Rotate(Rotation.Mul(lower, Rotation.Inverse(chain.LowerRotation)),
                    chain.EndPosition - chain.LowerPosition);
            }

            return new TwoBoneChain(a, b, c, upper, lower, chain.EndRotation);
        }

        /// <summary>
        /// Past the soft zone the reachable distance approaches full length asymptotically, so the joint straightens
        /// smoothly instead of popping at full extension.
        /// </summary>
        public static double SoftDistance(double distance, double length, double softness)
        {
            double soft = length * softness;
            double hard = length - soft;
            if (!(soft > 0.0) || distance <= hard)
            {
                return math.min(distance, length);
            }

            return hard + soft * (1.0 - DetMath.Exp(-(distance - hard) / soft));
        }
    }
}
