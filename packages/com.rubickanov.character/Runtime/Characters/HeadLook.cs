using System.Collections.Generic;
using Rubickanov.Rig;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// Turns the spine, neck and head toward where the character looks. Every bone chases its share
    /// through its own spring, faster toward the head, so the head leads and the body follows.
    /// The spine adds its share on top of the animation; the aiming bones then point the head at the
    /// look itself, so a clip that tilts the head does not tilt the gaze others see.
    /// While the legs turn the model in place, the chain turns back against them, so the upper body
    /// keeps facing where it looked and the hips come round under it.
    /// </summary>
    public sealed class HeadLook
    {
        // A larger turn of the model between two frames is a snap, not something to hold the upper body against.
        private const float MaxModelTurnPerFrame = 45f;

        private sealed class Bone
        {
            public Transform Transform;
            // Entry in CharacterRigConfig.LookBones, read every frame so shares can be tuned live.
            public int Entry;
            // 0 at the lowest bone, 1 at the head: picks the spring between SpineSpring and HeadSpring.
            public float Height;
            public FloatSpring Yaw, Pitch;
        }

        private readonly Transform _body;
        private readonly Transform _head;
        // The head's facing in its own space, taken from the avatar's rest pose, where the model faces forward.
        private readonly Vector3 _headForward;
        private readonly List<Bone> _bones = new();
        private FloatSpring _weight;
        // Where the head points when aiming: the look through the head's spring.
        private FloatSpring _aimYaw, _aimPitch;

        public bool IsValid => _bones.Count > 0;

        public HeadLook(Animator animator, Transform body, CharacterRigConfig.LookBone[] bones)
        {
            _body = body;
            for (int i = 0; i < bones.Length; i++)
            {
                Transform transform = animator.GetBoneTransform(bones[i].Bone);
                if (transform != null)
                {
                    _bones.Add(new Bone { Transform = transform, Entry = i });
                }
            }

            for (int i = 0; i < _bones.Count; i++)
            {
                _bones[i].Height = _bones.Count > 1 ? i / (_bones.Count - 1f) : 1f;
            }

            _head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (_head != null)
            {
                _headForward = RestForward(animator, _head);
            }
        }

        public void Reset()
        {
            _weight.Reset(0f);
            _aimYaw.Reset(0f);
            _aimPitch.Reset(0f);
            foreach (Bone bone in _bones)
            {
                bone.Yaw.Reset(0f);
                bone.Pitch.Reset(0f);
            }
        }

        /// <summary>
        /// Aim is the look direction in the character's space, pitch included. Zero looks ahead.
        /// ModelTurn is how far the model turned in place since the last call, in degrees positive to the right.
        /// </summary>
        public void Apply(CharacterRigConfig config, Vector3 aim, float modelTurn, float weight, float deltaTime)
        {
            if (Mathf.Abs(modelTurn) <= MaxModelTurnPerFrame)
            {
                Counter(config, modelTurn);
            }

            bool looking = config.Look && aim.sqrMagnitude > 1e-6f;
            weight *= Mathf.Clamp01(_weight.Step(looking ? 1f : 0f, config.HeadSpring, deltaTime));
            AimAngles angles = looking ? Limit(config, AimAngles.FromLocal(aim)) : default;
            float aimYaw = _aimYaw.Step(angles.Yaw, config.HeadSpring, deltaTime);
            float aimPitch = _aimPitch.Step(angles.Pitch, config.HeadSpring, deltaTime);

            Vector3 up = _body.up;
            Quaternion facing = _body.rotation;
            float turned = 0f;
            float aimingYaw = 0f, aimingPitch = 0f;
            foreach (Bone bone in _bones)
            {
                if (bone.Entry >= config.LookBones.Length)
                {
                    break;
                }

                CharacterRigConfig.LookBone share = config.LookBones[bone.Entry];
                var spring = new SpringSettings(
                    Mathf.Lerp(config.SpineSpring.Frequency, config.HeadSpring.Frequency, bone.Height),
                    Mathf.Lerp(config.SpineSpring.Damping, config.HeadSpring.Damping, bone.Height));
                float yaw = bone.Yaw.Step(angles.Yaw * share.Yaw, spring, deltaTime) * weight;
                float pitch = bone.Pitch.Step(angles.Pitch * share.Pitch, spring, deltaTime) * weight;
                if (share.Aims && _head != null)
                {
                    aimingYaw += share.Yaw;
                    aimingPitch += share.Pitch;
                    continue;
                }

                turned += yaw;
                // Pitch about the side axis as the bones below have already turned it.
                Vector3 right = Quaternion.AngleAxis(turned, up) * (facing * Vector3.right);
                bone.Transform.rotation = Quaternion.AngleAxis(yaw, up)
                                          * Quaternion.AngleAxis(-pitch, right)
                                          * bone.Transform.rotation;
            }

            if (aimingYaw <= 0f && aimingPitch <= 0f)
            {
                return;
            }

            // What still separates the head, as animated and turned by the spine, from the look.
            AimAngles head = HeadAngles();
            float missingYaw = Mathf.DeltaAngle(head.Yaw, aimYaw) * weight;
            float missingPitch = (aimPitch - head.Pitch) * weight;
            foreach (Bone bone in _bones)
            {
                if (bone.Entry >= config.LookBones.Length)
                {
                    break;
                }

                CharacterRigConfig.LookBone share = config.LookBones[bone.Entry];
                if (!share.Aims)
                {
                    continue;
                }

                float yaw = Mathf.Lerp(bone.Yaw.Value * weight,
                    aimingYaw > 0f ? missingYaw * share.Yaw / aimingYaw : 0f, config.HeadAim);
                float pitch = Mathf.Lerp(bone.Pitch.Value * weight,
                    aimingPitch > 0f ? missingPitch * share.Pitch / aimingPitch : 0f, config.HeadAim);
                // About the side axis of the head's own facing, pitch changes the head's pitch alone.
                Vector3 forward = Vector3.ProjectOnPlane(_head.rotation * _headForward, up);
                Vector3 right = forward.sqrMagnitude > 1e-6f
                    ? Vector3.Cross(up, forward).normalized
                    : facing * Vector3.right;
                bone.Transform.rotation = Quaternion.AngleAxis(yaw, up)
                                          * Quaternion.AngleAxis(-pitch, right)
                                          * bone.Transform.rotation;
            }
        }

        // The look is given in the model's space, so turning the model moves every target by as much.
        // Moving the springs with it keeps the chain where it faced; they then ease onto the new targets.
        private void Counter(CharacterRigConfig config, float modelTurn)
        {
            _aimYaw.Value -= modelTurn;
            foreach (Bone bone in _bones)
            {
                if (bone.Entry < config.LookBones.Length)
                {
                    bone.Yaw.Value -= modelTurn * config.LookBones[bone.Entry].Yaw;
                }
            }
        }

        private AimAngles Limit(CharacterRigConfig config, AimAngles look)
        {
            float yaw = look.Yaw;
            // Near straight behind, stay over the shoulder the head is already turned to.
            if (Mathf.Abs(yaw) > 180f - config.ShoulderHold && Mathf.Abs(_aimYaw.Value) > 1f
                && Mathf.Sign(yaw) != Mathf.Sign(_aimYaw.Value))
            {
                yaw = -yaw;
            }

            return new AimAngles(yaw, look.Pitch).Limit(config.YawLimit, config.PitchUp, config.PitchDown);
        }

        public void DrawGizmos(Vector3 aim)
        {
            if (_bones.Count == 0)
            {
                return;
            }

            Vector3 head = _bones[^1].Transform.position;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(head, head + _body.rotation * aim.normalized);
            if (_head != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(_head.position, _head.position + _head.rotation * _headForward * 0.75f);
            }
        }

        private AimAngles HeadAngles() =>
            AimAngles.FromLocal(Quaternion.Inverse(_body.rotation) * (_head.rotation * _headForward));

        // The avatar's skeleton holds the rest pose it was configured in, facing the model's forward.
        // Bones missing from it fall back to their current rotation.
        private static Vector3 RestForward(Animator animator, Transform head)
        {
            SkeletonBone[] skeleton = animator.avatar != null ? animator.avatar.humanDescription.skeleton : null;
            Quaternion rest = Quaternion.identity;
            for (Transform bone = head; bone != null && bone != animator.transform; bone = bone.parent)
            {
                rest = RestRotation(skeleton, bone) * rest;
            }

            return (Quaternion.Inverse(rest) * Vector3.forward).normalized;
        }

        /// <summary>
        /// Where <paramref name="bone"/> lies in the animator's own frame in the avatar's rest pose; the bones as they
        /// are now where the avatar does not describe them.
        /// </summary>
        public static void RestPose(Animator animator, Transform bone, out Vector3 position, out Quaternion rotation)
        {
            SkeletonBone[] skeleton = animator.avatar != null ? animator.avatar.humanDescription.skeleton : null;
            position = Vector3.zero;
            rotation = Quaternion.identity;
            var chain = new System.Collections.Generic.List<Transform>();
            for (Transform t = bone; t != null && t != animator.transform; t = t.parent)
            {
                chain.Add(t);
            }

            for (int i = chain.Count - 1; i >= 0; i--)
            {
                Transform t = chain[i];
                Vector3 local = t.localPosition;
                if (skeleton != null)
                {
                    foreach (SkeletonBone entry in skeleton)
                    {
                        if (entry.name == t.name)
                        {
                            local = entry.position;
                            break;
                        }
                    }
                }

                position += rotation * local;
                rotation *= RestRotation(skeleton, t);
            }
        }

        internal static Quaternion RestRotation(SkeletonBone[] skeleton, Transform bone)
        {
            if (skeleton != null)
            {
                foreach (SkeletonBone entry in skeleton)
                {
                    if (entry.name == bone.name)
                    {
                        return entry.rotation;
                    }
                }
            }

            return bone.localRotation;
        }
    }
}
