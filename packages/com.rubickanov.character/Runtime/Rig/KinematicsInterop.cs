using Rubickanov.Kinematics;
using Unity.Mathematics;
using UnityEngine;

namespace Rubickanov.Rig
{
    /// <summary>
    /// Where the character's float world meets com.rubickanov.kinematics, which works on double: vectors and
    /// rotations both ways, the two-bone solver on transforms and springs over float values. The maths is all
    /// the package's; nothing here but conversion.
    /// </summary>
    public static class KinematicsInterop
    {
        public static double3 ToDouble3(this Vector3 v) => new(v.x, v.y, v.z);

        public static Vector3 ToVector3(this double3 v) => new((float)v.x, (float)v.y, (float)v.z);

        public static double4 ToDouble4(this Quaternion q) => new(q.x, q.y, q.z, q.w);

        public static Quaternion ToQuaternion(this double4 q) =>
            new((float)q.x, (float)q.y, (float)q.z, (float)q.w);

        /// <summary>
        /// <see cref="TwoBoneIK.Solve"/> on three bones: bends <paramref name="upper"/> and <paramref name="lower"/>
        /// so that <paramref name="end"/> reaches the target, the middle joint toward the hint; the end keeps its
        /// world rotation.
        /// </summary>
        public static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint,
            float weight, double softness = TwoBoneIK.Softness)
        {
            if (!(weight > 0f))
            {
                return;
            }

            var chain = new TwoBoneChain(upper.position.ToDouble3(), lower.position.ToDouble3(),
                end.position.ToDouble3(), upper.rotation.ToDouble4(), lower.rotation.ToDouble4(),
                end.rotation.ToDouble4());
            TwoBoneChain solved = TwoBoneIK.Solve(chain, target.ToDouble3(), hint.ToDouble3(), weight, softness);
            Quaternion endRotation = end.rotation;
            upper.rotation = solved.UpperRotation.ToQuaternion();
            lower.rotation = solved.LowerRotation.ToQuaternion();
            end.rotation = endRotation;
        }

        /// <summary><see cref="SpringStep.Apply(ref double, ref double, double)"/> on a float and its rate.</summary>
        public static void Apply(this SpringStep step, ref float value, ref float velocity, float target)
        {
            double v = value;
            double rate = velocity;
            step.Apply(ref v, ref rate, target);
            value = (float)v;
            velocity = (float)rate;
        }

        /// <summary><see cref="SpringStep.Apply(ref double3, ref double3, double3)"/> on a vector and its rate.</summary>
        public static void Apply(this SpringStep step, ref Vector3 value, ref Vector3 velocity, Vector3 target)
        {
            double3 v = value.ToDouble3();
            double3 rate = velocity.ToDouble3();
            step.Apply(ref v, ref rate, target.ToDouble3());
            value = v.ToVector3();
            velocity = rate.ToVector3();
        }
    }
}
