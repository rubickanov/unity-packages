using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// An extra sway the game lays on the animated pose (<see cref="CharacterRig.Sway"/>), e.g. a drunk's
    /// unsteady balance. Default (all zero) leaves the rig exactly as without it.
    /// </summary>
    public struct RigSway
    {
        /// <summary>World, horizontal: where the top of the body tips; length is the angle in degrees.</summary>
        public Vector3 Lean;
        /// <summary>The same, laid on the head on top of the look.</summary>
        public Vector3 HeadLean;
        /// <summary>0..1: share of <see cref="Lean"/> the upper arms take back, so they hang plumb.</summary>
        public float ArmHang;
        /// <summary>Metres the feet are set wider apart while on the ground.</summary>
        public float Stance;
        /// <summary>World, horizontal, degrees: laid on the chest on top of <see cref="Lean"/> (the chain lagging it).</summary>
        public Vector3 ChestLean;
        /// <summary>
        /// Degrees on each upper arm after the hang: x raises it out to its side, y forward, z bends the elbow.
        /// </summary>
        public Vector3 LeftArm;
        public Vector3 RightArm;
        /// <summary>Metres the pelvis sinks while the feet hold the ground: the knees give.</summary>
        public float Sink;

        public bool IsZero => Lean.sqrMagnitude == 0f && HeadLean.sqrMagnitude == 0f && ArmHang == 0f && Stance == 0f
                              && ChestLean.sqrMagnitude == 0f && LeftArm.sqrMagnitude == 0f
                              && RightArm.sqrMagnitude == 0f && Sink == 0f;

        /// <summary>A rotation tipping the top of the body by <paramref name="lean"/> (degrees, world).</summary>
        public static Quaternion Tip(Vector3 lean, Vector3 up)
        {
            Vector3 flat = Vector3.ProjectOnPlane(lean, up);
            float angle = flat.magnitude;
            return angle > 1e-6f ? Quaternion.AngleAxis(angle, Vector3.Cross(up, flat / angle)) : Quaternion.identity;
        }
    }
}
