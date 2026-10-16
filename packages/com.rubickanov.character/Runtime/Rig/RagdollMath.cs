using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Rig
{
    /// <summary>
    /// The ragdoll's maths, apart from any model so tests can call it: a joint's target rotation from an
    /// animated one, where a visible bone goes to show its ragdoll bone, and how a body lying on the ground
    /// stands up into a clip.
    /// </summary>
    public static class RagdollMath
    {
        /// <summary>
        /// A ConfigurableJoint's own axes in its body's axes: x along <paramref name="axis"/>, y along
        /// <paramref name="secondaryAxis"/> made square to it, z their cross product.
        /// </summary>
        public static Quaternion JointSpace(Vector3 axis, Vector3 secondaryAxis)
        {
            Vector3 forward = Vector3.Cross(axis, secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, axis).normalized;
            return Quaternion.LookRotation(forward, up);
        }

        /// <summary>
        /// The target rotation that turns a joint's body to <paramref name="local"/> relative to the connected
        /// body. A joint's target is in its own axes and relative to the pose it was made in, which is
        /// <paramref name="restLocal"/>. Identity when <paramref name="local"/> is the rest.
        /// </summary>
        public static Quaternion TargetRotation(Quaternion jointSpace, Quaternion restLocal, Quaternion local) =>
            Quaternion.Inverse(jointSpace) * Quaternion.Inverse(local) * restLocal * jointSpace;

        /// <summary>
        /// Where a visible bone goes to show its ragdoll bone, at <paramref name="weight"/> between its animated
        /// pose (0) and the ragdoll's (1). It takes the ragdoll bone's rotation, and its position relative to the
        /// connected ragdoll bone is put on that bone's visible counterpart, so the skeleton keeps its own lengths.
        /// Offsets turn a visible bone's axes into its ragdoll bone's. A bone with no connected bone (the hips)
        /// takes the ragdoll bone's position.
        /// </summary>
        public static (Vector3 Position, Quaternion Rotation) MapPose(Vector3 animatedPosition,
            Quaternion animatedRotation, Vector3 bonePosition, Quaternion boneRotation, Quaternion offset,
            bool hasParent, Vector3 parentPosition, Quaternion parentRotation, Vector3 visibleParentPosition,
            Quaternion visibleParentRotation, Quaternion parentOffset, float weight)
        {
            Vector3 position = bonePosition;
            if (hasParent)
            {
                Vector3 local = Quaternion.Inverse(parentRotation) * (bonePosition - parentPosition);
                position = visibleParentPosition + visibleParentRotation * parentOffset * local;
            }

            Quaternion rotation = boneRotation * Quaternion.Inverse(offset);
            return (Vector3.Lerp(animatedPosition, position, weight),
                Quaternion.Slerp(animatedRotation, rotation, weight));
        }

        /// <summary>The angular velocity that turns <paramref name="from"/> into <paramref name="to"/> in <paramref name="dt"/>, rad/s.</summary>
        public static Vector3 AngularVelocity(Quaternion from, Quaternion to, float dt)
        {
            if (dt <= 0f)
            {
                return Vector3.zero;
            }

            Quaternion delta = to * Quaternion.Inverse(from);
            if (delta.w < 0f)
            {
                delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            }

            delta.ToAngleAxis(out float degrees, out Vector3 axis);
            if (degrees < 1e-4f || float.IsInfinity(axis.x) || float.IsNaN(axis.x))
            {
                return Vector3.zero;
            }

            return axis * (degrees * Mathf.Deg2Rad / dt);
        }

        /// <summary>
        /// How many joints each bone is from bone <paramref name="from"/> along the skeleton, -1 for those more
        /// than <paramref name="reach"/> away. <paramref name="parents"/> gives each bone's connected bone, -1 for
        /// the root.
        /// </summary>
        public static void Hops(IReadOnlyList<int> parents, int from, int reach, IList<int> hops)
        {
            for (int i = 0; i < parents.Count; i++)
            {
                hops[i] = -1;
            }

            hops[from] = 0;
            for (int step = 0; step < reach; step++)
            {
                for (int i = 0; i < parents.Count; i++)
                {
                    int parent = parents[i];
                    if (parent < 0)
                    {
                        continue;
                    }

                    if (hops[parent] == step && hops[i] < 0)
                    {
                        hops[i] = step + 1;
                    }
                    else if (hops[i] == step && hops[parent] < 0)
                    {
                        hops[parent] = step + 1;
                    }
                }
            }
        }

        /// <summary>
        /// Spreads a push of <paramref name="impulse"/> (N·s) from the bone it hits over the bones near it, as a
        /// blow carries into the body around it: a bone <paramref name="hops"/> joints away changes speed by
        /// <paramref name="falloff"/> to that power of the hit bone's, a bone at -1 not at all. The hit bone goes
        /// no faster than <paramref name="maxSpeed"/>, so a light limb is not torn away; the impulse that leaves
        /// over is returned, for the whole body to carry. <paramref name="speeds"/> gets each bone's change, m/s.
        /// </summary>
        public static float SpreadPush(IReadOnlyList<float> masses, IReadOnlyList<int> hops, float falloff,
            float impulse, float maxSpeed, IList<float> speeds)
        {
            float weighted = 0f;
            for (int i = 0; i < masses.Count; i++)
            {
                if (hops[i] >= 0)
                {
                    weighted += masses[i] * Mathf.Pow(falloff, hops[i]);
                }
            }

            float speed = weighted > 0f ? Mathf.Min(impulse / weighted, maxSpeed) : 0f;
            for (int i = 0; i < masses.Count; i++)
            {
                speeds[i] = hops[i] >= 0 ? speed * Mathf.Pow(falloff, hops[i]) : 0f;
            }

            return impulse - speed * weighted;
        }

        /// <summary>Whether a body lying down faces the sky: its hips' forward points up rather than down.</summary>
        public static bool LiesFaceUp(Vector3 hipsForward, Vector3 up) => Vector3.Dot(hipsForward, up) >= 0f;

        /// <summary>
        /// The rotation a character's model needs to start a get-up clip from a body lying on the ground.
        /// <paramref name="clipHipsToHead"/> is the hips-to-head direction on the clip's first frame in the
        /// model's space, <paramref name="hipsToHead"/> the lying body's in the world. Only the heading counts:
        /// the result stands upright on <paramref name="up"/>. Falls back to <paramref name="fallback"/> when
        /// either direction is along up.
        /// </summary>
        public static Quaternion GetUpRotation(Vector3 clipHipsToHead, Vector3 hipsToHead, Vector3 up,
            Quaternion fallback)
        {
            Vector3 clip = Vector3.ProjectOnPlane(clipHipsToHead, Vector3.up);
            Vector3 lying = Vector3.ProjectOnPlane(hipsToHead, up);
            if (clip.sqrMagnitude < 1e-6f || lying.sqrMagnitude < 1e-6f)
            {
                return fallback;
            }

            return Quaternion.LookRotation(lying, up) * Quaternion.Inverse(Quaternion.LookRotation(clip, Vector3.up));
        }

        // The three smallest components of a unit quaternion lie within this of zero.
        private const float PackRange = 0.70710678f;
        private const uint PackSteps = 1023;

        /// <summary>
        /// A rotation in four bytes: which component is largest in two bits, and the other three in ten bits each.
        /// The largest is rebuilt from the rest, its sign dropped since q and -q are the same rotation. Off by at
        /// most about a fifth of a degree.
        /// </summary>
        public static uint PackRotation(Quaternion rotation)
        {
            rotation = rotation.normalized;
            int largest = 0;
            for (int i = 1; i < 4; i++)
            {
                if (Mathf.Abs(rotation[i]) > Mathf.Abs(rotation[largest]))
                {
                    largest = i;
                }
            }

            float sign = rotation[largest] < 0f ? -1f : 1f;
            uint packed = (uint)largest;
            int shift = 2;
            for (int i = 0; i < 4; i++)
            {
                if (i == largest)
                {
                    continue;
                }

                float value = Mathf.Clamp(rotation[i] * sign, -PackRange, PackRange);
                packed |= (uint)Mathf.RoundToInt((value + PackRange) / (2f * PackRange) * PackSteps) << shift;
                shift += 10;
            }

            return packed;
        }

        /// <summary>The rotation <see cref="PackRotation"/> packed.</summary>
        public static Quaternion UnpackRotation(uint packed)
        {
            int largest = (int)(packed & 3u);
            Quaternion rotation = default;
            float sum = 0f;
            int shift = 2;
            for (int i = 0; i < 4; i++)
            {
                if (i == largest)
                {
                    continue;
                }

                float value = (packed >> shift & PackSteps) / (float)PackSteps * 2f * PackRange - PackRange;
                rotation[i] = value;
                sum += value * value;
                shift += 10;
            }

            rotation[largest] = Mathf.Sqrt(Mathf.Max(0f, 1f - sum));
            return rotation.normalized;
        }
    }
}
