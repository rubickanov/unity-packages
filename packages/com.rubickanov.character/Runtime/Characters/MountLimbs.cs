using Rubickanov.Rig;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// Hands and feet on the holds of the seat a character rides in (<see cref="ICharacterMount"/>), such as a
    /// sled's rope or a plough's footboard. Each limb with a target reaches it, bending as the pose bends it; a limb
    /// without one stays as the clip has it; a hand on a hold closes into the seat's hand shape. Reaches in over <see cref="CharacterRigConfig.MountReachTime"/> after
    /// sitting down rather than snapping onto the holds.
    /// </summary>
    public sealed class MountLimbs
    {
        private sealed class Limb
        {
            public Transform Upper, Middle, End;
        }

        // A hand's shape, measured from the bones: where the palm is and which way it faces.
        private sealed class Palm
        {
            // The hand's rotation in the forearm's space with the wrist straight.
            public Quaternion Rest;
            // Along the forearm, in the straight hand's space.
            public Vector3 Axis;
            // From a grip's frame (forward the way the palm faces, up along the fingers) to the hand's.
            public Quaternion FromGrip;
            // The palm's middle and the way it faces, in the hand's space.
            public Vector3 Middle, Facing;
            // The finger block and the thumb, their rest turns under the hand, and the axes (hand space) the hand
            // shapes turn them about: curl toward the palm, thumb fold over it, thumb spread off the fingers.
            public Transform Index, Thumb;
            public Quaternion IndexRest, ThumbRest;
            public Vector3 CurlAxis, FoldAxis, SpreadAxis;
        }

        private readonly Limb _leftHand;
        private readonly Limb _rightHand;
        private readonly Palm _leftPalm;
        private readonly Palm _rightPalm;
        private readonly Limb _leftFoot;
        private readonly Limb _rightFoot;
        // 0 when the character has just sat down, 1 once the limbs are on the holds.
        private float _reach;
        // The last bend and twist each wrist was asked for.
        private Vector2 _askedLeft, _askedRight;

        // Where the palm's middle lies from the wrist to the index's root, as the hand pieces are built.
        private const float PalmAlong = 0.55f;

        private readonly WorkHandReach _leftWork = new();
        private readonly WorkHandReach _rightWork = new();

        public void ApplyWork(IWorkHands targets, float deltaTime, float weight = 1f,
            ICharacterMount mount = null, bool suppressed = false)
        {
            bool valid = targets != null && !(targets is Object obj && obj == null)
                && !(targets is Behaviour behaviour && !behaviour.isActiveAndEnabled);
            bool mounted = mount != null && !(mount is Object seat && seat == null);
            bool free = !suppressed && (!mounted || mount.HandsFree);
            _leftWork.Step(valid ? targets.Left : null, valid ? targets.Weight : 0, weight,
                free && (!mounted || mount.LeftHand == null), deltaTime);
            _rightWork.Step(valid ? targets.Right : null, valid ? targets.Weight : 0, weight,
                free && (!mounted || mount.RightHand == null), deltaTime);
            Reach(_leftHand, _leftWork.Position, _leftWork.Weight);
            Reach(_rightHand, _rightWork.Position, _rightWork.Weight);
        }

        public bool IsValid => _leftHand != null || _rightHand != null || _leftFoot != null || _rightFoot != null;

        public MountLimbs(Animator animator)
        {
            _leftHand = Create(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand);
            _rightHand = Create(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand);
            _leftPalm = CreatePalm(animator, _leftHand, false);
            _rightPalm = CreatePalm(animator, _rightHand, true);
            _leftFoot = Create(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot);
            _rightFoot = Create(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot);
        }

        /// <summary>Lets go of the holds: the next seat is reached for from the clip's pose.</summary>
        public void Reset()
        {
            _reach = 0f;
            _leftWork.Reset();
            _rightWork.Reset();
        }

        /// <summary>
        /// Puts the hands on the seat's hand holds (the wrists or the palms, by <see cref="CharacterRigConfig.MountGrip"/>)
        /// and the feet on its foot targets, over the animated pose.
        /// </summary>
        public void Apply(CharacterRigConfig config, ICharacterMount mount, float weight, float deltaTime)
        {
            // A seat that was destroyed still answers as an interface; its targets went with it.
            if (mount == null || (mount is Object seat && seat == null))
            {
                return;
            }

            float time = config.MountReachTime;
            _reach = time > 0f ? Mathf.MoveTowards(_reach, 1f, deltaTime / time) : 1f;
            weight *= Mathf.SmoothStep(0f, 1f, _reach);
            if (!config.MountReach || weight <= 1e-3f)
            {
                return;
            }

            Reach(_leftFoot, mount.LeftFoot, weight);
            Reach(_rightFoot, mount.RightFoot, weight);
            if (config.MountGrip == MountGrip.Wrist)
            {
                Reach(_leftHand, mount.LeftHand, weight);
                Reach(_rightHand, mount.RightHand, weight);
                return;
            }

            GripHand(false, mount.LeftHand, config.MountPalmDepth, config.MountWristBend, config.MountWristTwist, weight);
            GripHand(true, mount.RightHand, config.MountPalmDepth, config.MountWristBend, config.MountWristTwist, weight);
            if (mount.LeftHand != null)
            {
                ShapeFingers(false, config.Shape(mount.LeftHandShape), weight);
            }

            if (mount.RightHand != null)
            {
                ShapeFingers(true, config.Shape(mount.RightHandShape), weight);
            }
        }

        /// <summary>
        /// Turns the right or left hand's fingers from the pose drawn now toward <paramref name="shape"/> by
        /// <paramref name="weight"/>. Set from the rest each time, so it never adds up over frames.
        /// </summary>
        public void ShapeFingers(bool right, in HandShapePose shape, float weight)
        {
            Palm palm = right ? _rightPalm : _leftPalm;
            if (palm == null || palm.Index == null || weight <= 0f)
            {
                return;
            }

            Quaternion index = Quaternion.AngleAxis(shape.Curl, palm.CurlAxis) * palm.IndexRest;
            palm.Index.localRotation = Quaternion.Slerp(palm.Index.localRotation, index, weight);
            if (palm.Thumb != null)
            {
                Quaternion thumb = Quaternion.AngleAxis(shape.ThumbSpread, palm.SpreadAxis)
                                   * Quaternion.AngleAxis(shape.ThumbFold, palm.FoldAxis) * palm.ThumbRest;
                palm.Thumb.localRotation = Quaternion.Slerp(palm.Thumb.localRotation, thumb, weight);
            }
        }

        /// <summary>
        /// Puts one palm on <paramref name="grip"/>: its middle, lifted by <paramref name="depth"/> off the palm's
        /// face, at the grip's position, the palm facing along the grip's forward, the fingers on from the forearm
        /// (along the grip's up when the forearm points into the palm). The wrist
        /// bends at most <paramref name="maxBend"/> degrees off the forearm and twists at most
        /// <paramref name="maxTwist"/>; past them the palm stays as close as the wrist allows. The elbow points
        /// toward <paramref name="hint"/> when given, else where the pose points it. False without such a hand.
        /// </summary>
        public bool GripHand(bool right, Transform grip, float depth, float maxBend, float maxTwist, float weight,
            Vector3? hint = null)
        {
            Limb limb = right ? _rightHand : _leftHand;
            Palm palm = right ? _rightPalm : _leftPalm;
            if (limb == null || palm == null || grip == null || weight <= 0f)
            {
                return false;
            }

            Quaternion upperStart = limb.Upper.rotation;
            Quaternion middleStart = limb.Middle.rotation;
            Quaternion endStart = limb.End.rotation;
            Quaternion wanted = grip.rotation * palm.FromGrip;
            Vector3 contact = palm.Middle + palm.Facing * depth;
            // Reach with the hand as the grip has it, then again with the hand as the forearm there lays it.
            Quaternion hand = wanted;
            for (int pass = 0; pass < 3; pass++)
            {
                limb.Upper.rotation = upperStart;
                limb.Middle.rotation = middleStart;
                Reach(limb, grip.position - hand * contact, hint);
                wanted = Along(limb, palm, grip);
                hand = Wrist(limb, palm, wanted, maxBend, maxTwist);
            }

            if (weight < 1f)
            {
                limb.Upper.rotation = Quaternion.Slerp(upperStart, limb.Upper.rotation, weight);
                limb.Middle.rotation = Quaternion.Slerp(middleStart, limb.Middle.rotation, weight);
            }

            limb.End.rotation = Quaternion.Slerp(endStart, Wrist(limb, palm, wanted, maxBend, maxTwist), weight);
            Ask(right, WristTurn(limb, palm, wanted, out float asked), asked);
            return true;
        }

        /// <summary>
        /// Puts the right or left hand bone at <paramref name="position"/> turned to <paramref name="rotation"/>: the
        /// arm reaches with its elbow toward <paramref name="hint"/>, the wrist turns the hand as near to the rotation
        /// as <paramref name="maxBend"/> and <paramref name="maxTwist"/> let it. False without such a hand.
        /// </summary>
        public bool HoldHand(bool right, Vector3 position, Quaternion rotation, Vector3 hint, float maxBend,
            float maxTwist, float weight)
        {
            Limb limb = right ? _rightHand : _leftHand;
            Palm palm = right ? _rightPalm : _leftPalm;
            if (limb == null || palm == null || weight <= 0f)
            {
                return false;
            }

            Quaternion upperStart = limb.Upper.rotation;
            Quaternion middleStart = limb.Middle.rotation;
            Quaternion endStart = limb.End.rotation;
            TwoBoneIK.Solve(limb.Upper, limb.Middle, limb.End, position, hint, 1f);
            if (weight < 1f)
            {
                limb.Upper.rotation = Quaternion.Slerp(upperStart, limb.Upper.rotation, weight);
                limb.Middle.rotation = Quaternion.Slerp(middleStart, limb.Middle.rotation, weight);
            }

            limb.End.rotation = Quaternion.Slerp(endStart, Wrist(limb, palm, rotation, maxBend, maxTwist), weight);
            HoldBend = WristTurn(limb, palm, rotation, out float twist);
            HoldTwist = twist;
            Ask(right, HoldBend, HoldTwist);
            return true;
        }

        /// <summary>How far the last grip or hold of that hand asked its wrist to bend, degrees, before the limit.</summary>
        public float AskedBend(bool right) => right ? _askedRight.x : _askedLeft.x;

        /// <summary>How far the last grip or hold of that hand asked its wrist to twist, degrees, before the limit.</summary>
        public float AskedTwist(bool right) => right ? _askedRight.y : _askedLeft.y;

        private void Ask(bool right, float bend, float twist)
        {
            if (right)
            {
                _askedRight = new Vector2(bend, twist);
            }
            else
            {
                _askedLeft = new Vector2(bend, twist);
            }
        }

        /// <summary>How far the last <see cref="HoldHand"/> asked the wrist to bend and twist, degrees, before limits.</summary>
        public float HoldBend { get; private set; }

        public float HoldTwist { get; private set; }

        private static float WristTurn(Limb limb, Palm palm, Quaternion wanted, out float twist)
        {
            Quaternion straight = limb.Middle.rotation * palm.Rest;
            Quaternion turn = Quaternion.Inverse(straight) * wanted;
            Vector3 along = Vector3.Project(new Vector3(turn.x, turn.y, turn.z), palm.Axis);
            var part = new Quaternion(along.x, along.y, along.z, turn.w);
            float length = Mathf.Sqrt(part.x * part.x + part.y * part.y + part.z * part.z + part.w * part.w);
            Quaternion around = length > 1e-4f ? Normalized(part) : Quaternion.identity;
            twist = Quaternion.Angle(Quaternion.identity, around);
            return Quaternion.Angle(Quaternion.identity, turn * Quaternion.Inverse(around));
        }

        /// <summary>
        /// Where a grip lies on the right or left hand when the palm holds it: its point off the hand bone, in the
        /// bone's turn (unscaled), and the turn from the grip's frame to the hand's. False without a measured palm.
        /// </summary>
        public bool GripOnHand(bool right, float depth, out Vector3 offset, out Quaternion fromGrip)
        {
            Limb limb = right ? _rightHand : _leftHand;
            Palm palm = right ? _rightPalm : _leftPalm;
            offset = Vector3.zero;
            fromGrip = Quaternion.identity;
            if (limb == null || palm == null)
            {
                return false;
            }

            Vector3 point = limb.End.TransformPoint(palm.Middle + palm.Facing * depth);
            offset = Quaternion.Inverse(limb.End.rotation) * (point - limb.End.position);
            fromGrip = palm.FromGrip;
            return true;
        }

        /// <summary>
        /// How far past the arm's reach the wrist would have to go for the palm to lie on <paramref name="grip"/>,
        /// metres (negative within reach), and from where: the shoulder. False without such a hand.
        /// </summary>
        public bool Overreach(bool right, Transform grip, float depth, out float overreach, out Vector3 shoulder)
        {
            Limb limb = right ? _rightHand : _leftHand;
            Palm palm = right ? _rightPalm : _leftPalm;
            overreach = 0f;
            shoulder = Vector3.zero;
            if (limb == null || palm == null || grip == null)
            {
                return false;
            }

            shoulder = limb.Upper.position;
            Vector3 wrist = grip.position - grip.rotation * palm.FromGrip * (palm.Middle + palm.Facing * depth);
            float reach = Vector3.Distance(limb.Upper.position, limb.Middle.position)
                          + Vector3.Distance(limb.Middle.position, limb.End.position);
            overreach = Vector3.Distance(shoulder, wrist) - reach;
            return true;
        }

        /// <summary>Where the right or left palm's face is now, lifted by depth; the wrist without a measured palm.</summary>
        public Vector3 PalmPoint(bool right, float depth)
        {
            Limb limb = right ? _rightHand : _leftHand;
            Palm palm = right ? _rightPalm : _leftPalm;
            if (limb == null)
            {
                return Vector3.zero;
            }

            return palm != null ? limb.End.TransformPoint(palm.Middle + palm.Facing * depth) : limb.End.position;
        }

        /// <summary>How far the right or left wrist is bent off its forearm now, degrees (twist left out).</summary>
        public float WristBend(bool right)
        {
            Limb limb = right ? _rightHand : _leftHand;
            Palm palm = right ? _rightPalm : _leftPalm;
            if (limb == null || palm == null)
            {
                return 0f;
            }

            Quaternion straight = limb.Middle.rotation * palm.Rest;
            Vector3 along = straight * palm.Axis;
            return Vector3.Angle(along, limb.End.rotation * palm.Axis);
        }

        // The palm on the grip with the fingers carrying on from the forearm, as near straight as the grip allows.
        private static Quaternion Along(Limb limb, Palm palm, Transform grip)
        {
            Vector3 facing = grip.forward;
            Vector3 fingers = Vector3.ProjectOnPlane(limb.End.position - limb.Middle.position, facing);
            if (fingers.sqrMagnitude < 0.04f * (limb.End.position - limb.Middle.position).sqrMagnitude)
            {
                fingers = grip.up;
            }

            return Quaternion.LookRotation(facing, fingers) * palm.FromGrip;
        }

        // The hand's rotation nearest to wanted that the wrist reaches from the forearm as posed now.
        private static Quaternion Wrist(Limb limb, Palm palm, Quaternion wanted, float maxBend, float maxTwist)
        {
            Quaternion straight = limb.Middle.rotation * palm.Rest;
            Quaternion turn = Quaternion.Inverse(straight) * wanted;
            // Split into a twist about the forearm and the bend left over.
            Vector3 part = new Vector3(turn.x, turn.y, turn.z);
            Vector3 along = Vector3.Project(part, palm.Axis);
            var twist = new Quaternion(along.x, along.y, along.z, turn.w);
            if (twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w < 1e-8f)
            {
                twist = Quaternion.identity;
            }
            else
            {
                twist = Normalized(twist);
            }

            Quaternion bend = turn * Quaternion.Inverse(twist);
            return straight * Clamp(bend, maxBend) * Clamp(twist, maxTwist);
        }

        private static Quaternion Clamp(Quaternion rotation, float maxAngle)
        {
            rotation.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
            {
                angle -= 360f;
            }

            return Mathf.Abs(angle) <= maxAngle || !float.IsFinite(axis.x)
                ? rotation
                : Quaternion.AngleAxis(Mathf.Sign(angle) * maxAngle, axis);
        }

        private static Quaternion Normalized(Quaternion q)
        {
            float length = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length);
        }

        // The palm from the finger bones: the fingers point from the wrist to the index's root, the thumb's root
        // lies to one side, and the palm faces away from the back of the hand between them.
        private static Palm CreatePalm(Animator animator, Limb limb, bool right)
        {
            if (limb == null)
            {
                return null;
            }

            Transform index = animator.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal)
                ?? animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            Transform thumb = animator.GetBoneTransform(right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
            if (index == null || thumb == null)
            {
                return null;
            }

            Vector3 knuckles = limb.End.InverseTransformPoint(index.position);
            Vector3 fingers = knuckles.normalized;
            Vector3 side = Vector3.ProjectOnPlane(limb.End.InverseTransformPoint(thumb.position), fingers).normalized;
            Vector3 facing = (right ? Vector3.Cross(fingers, side) : Vector3.Cross(side, fingers)).normalized;
            SkeletonBone[] skeleton = animator.avatar != null ? animator.avatar.humanDescription.skeleton : null;
            Quaternion rest = HeadLook.RestRotation(skeleton, limb.End);
            // The fingers turn under the hand bone itself; the axes are in its space.
            bool shaped = index.parent == limb.End && thumb.parent == limb.End;
            return new Palm
            {
                Index = shaped ? index : null,
                Thumb = shaped ? thumb : null,
                IndexRest = HeadLook.RestRotation(skeleton, index),
                ThumbRest = HeadLook.RestRotation(skeleton, thumb),
                CurlAxis = Vector3.Cross(fingers, facing).normalized,
                FoldAxis = Vector3.Cross(side, facing).normalized,
                SpreadAxis = Vector3.Cross(fingers, side).normalized,
                Rest = rest,
                Axis = (Quaternion.Inverse(rest) * limb.End.localPosition).normalized,
                FromGrip = Quaternion.Inverse(Quaternion.LookRotation(facing, fingers)),
                Middle = fingers * (knuckles.magnitude * PalmAlong),
                Facing = facing,
            };
        }

        private static void Reach(Limb limb, Transform target, float weight)
        {
            if (limb == null || target == null)
            {
                return;
            }

            Reach(limb, target.position, weight);
        }

        private static void Reach(Limb limb, Vector3 target, Vector3? hint)
        {
            if (hint.HasValue)
            {
                TwoBoneIK.Solve(limb.Upper, limb.Middle, limb.End, target, hint.Value, 1f);
            }
            else
            {
                Reach(limb, target, 1f);
            }
        }

        // The elbow or knee keeps pointing where the clip points it, as in GetUpContacts.
        private static void Reach(Limb limb, Vector3 target, float weight)
        {
            if (limb == null || weight <= 0f) return;
            Vector3 root = limb.Upper.position;
            Vector3 line = limb.End.position - root;
            Vector3 bend = line.sqrMagnitude > 1e-8f
                ? Vector3.ProjectOnPlane(limb.Middle.position - root, line.normalized)
                : limb.Upper.rotation * Vector3.forward;
            Vector3 hint = limb.Middle.position + bend;
            TwoBoneIK.Solve(limb.Upper, limb.Middle, limb.End, target, hint, weight);
        }

        private static Limb Create(Animator animator, HumanBodyBones upper, HumanBodyBones middle,
            HumanBodyBones end)
        {
            var limb = new Limb
            {
                Upper = animator.GetBoneTransform(upper),
                Middle = animator.GetBoneTransform(middle),
                End = animator.GetBoneTransform(end),
            };
            return limb.Upper != null && limb.Middle != null && limb.End != null ? limb : null;
        }
    }
}
