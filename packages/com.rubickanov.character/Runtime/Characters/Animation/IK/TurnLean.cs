using System.Collections.Generic;
using Rubickanov.Rig;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// Leans the body into turns like a runner does, forward when speeding up and back when slowing
    /// down. An underdamped spring makes it sway back as a turn ends; the head keeps its eyes level.
    /// </summary>
    public sealed class TurnLean
    {
        // A larger turn between two frames is a teleport or a snap, not something to lean into.
        private const float MaxTurnPerFrame = 45f;

        private sealed class Bone
        {
            public Transform Transform;
            // Entry in CharacterRigConfig.LeanBones, read every frame so shares can be tuned live.
            public int Entry;
        }

        private readonly Transform _body;
        private readonly Transform _hips;
        private readonly Transform _head;
        private readonly Transform _leftArm;
        private readonly Transform _rightArm;
        private readonly List<Bone> _bones = new();
        private Vector3 _lastForward;
        private FloatSpring _speed;
        private FloatSpring _roll;
        private FloatSpring _pitch;

        public bool IsValid => _bones.Count > 0;
        /// <summary>Current sideways lean in degrees, positive to the right.</summary>
        public float Roll => _roll.Value;

        public TurnLean(Animator animator, Transform body, CharacterRigConfig.LeanBone[] bones)
        {
            _body = body;
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _head = animator.GetBoneTransform(HumanBodyBones.Head);
            _leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            for (int i = 0; i < bones.Length; i++)
            {
                Transform transform = animator.GetBoneTransform(bones[i].Bone);
                if (transform != null)
                {
                    _bones.Add(new Bone { Transform = transform, Entry = i });
                }
            }
        }

        public void Reset(float planarSpeed)
        {
            _lastForward = PlanarForward();
            _speed.Reset(planarSpeed);
            _roll.Reset(0f);
            _pitch.Reset(0f);
        }

        public void Apply(CharacterRigConfig config, float planarSpeed, bool grounded, float weight, float deltaTime) =>
            Apply(config, planarSpeed, grounded, weight, deltaTime, default);

        /// <summary>
        /// As above, with an extra <paramref name="sway"/> added to the lean after its springs: the same bones and
        /// shares, the pelvis around the feet; the upper arms take its <see cref="RigSway.ArmHang"/> back.
        /// </summary>
        public void Apply(CharacterRigConfig config, float planarSpeed, bool grounded, float weight, float deltaTime,
            in RigSway sway)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            Vector3 up = _body.up;
            Vector3 forward = PlanarForward();
            float turn = Vector3.SignedAngle(_lastForward, forward, up);
            _lastForward = forward;
            if (Mathf.Abs(turn) > MaxTurnPerFrame)
            {
                turn = 0f;
            }

            // Smoothed speed; its spring velocity is the forward acceleration, free of per-step jitter.
            float speed = _speed.Step(planarSpeed, config.SpeedSpring, deltaTime);
            float sideways = turn / deltaTime * Mathf.Deg2Rad * speed;
            float onward = _speed.Velocity;

            bool leaning = config.Lean && grounded;
            float rollTarget = leaning ? LeanAngle(sideways, config.RollScale, config.MaxRoll, config) : 0f;
            float pitchTarget = leaning ? LeanAngle(onward, config.PitchScale, config.MaxPitch, config) : 0f;
            float roll = _roll.Step(rollTarget, config.LeanSpring, deltaTime) * weight;
            float pitch = _pitch.Step(pitchTarget, config.LeanSpring, deltaTime) * weight;
            Vector3 right = Vector3.Cross(up, forward);
            bool swaying = !sway.IsZero;
            float swayRoll = 0f, swayPitch = 0f;
            if (swaying)
            {
                swayRoll = Vector3.Dot(sway.Lean, right) * weight;
                swayPitch = Vector3.Dot(sway.Lean, forward) * weight;
                roll += swayRoll;
                pitch += swayPitch;
            }

            if (Mathf.Abs(roll) < 1e-3f && Mathf.Abs(pitch) < 1e-3f)
            {
                return;
            }

            if (_hips != null && config.HipShift > 0f)
            {
                // Leaning around the feet would carry the pelvis into the turn by this much.
                float height = Vector3.Dot(_hips.position - _body.position, up);
                _hips.position += right * (height * Mathf.Sin(roll * Mathf.Deg2Rad) * config.HipShift);
                if (swaying)
                {
                    // The sway tips forward and back about the feet too, and the pelvis sinks so the far leg reaches.
                    float tip = Mathf.Sqrt(swayRoll * swayRoll + swayPitch * swayPitch) * Mathf.Deg2Rad;
                    _hips.position += forward * (height * Mathf.Sin(swayPitch * Mathf.Deg2Rad) * config.HipShift)
                        - up * (height * (1f - Mathf.Cos(tip)) * config.HipShift);
                }
            }

            foreach (Bone bone in _bones)
            {
                if (bone.Entry >= config.LeanBones.Length)
                {
                    break;
                }

                float share = config.LeanBones[bone.Entry].Share;
                bone.Transform.rotation = Lean(roll * share, pitch * share, forward, right) * bone.Transform.rotation;
            }

            if (_head != null && config.HeadStabilize > 0f)
            {
                float keep = -config.HeadStabilize;
                _head.rotation = Lean(roll * keep, pitch * keep, forward, right) * _head.rotation;
            }

            if (swaying && sway.ArmHang > 0f)
            {
                // The arms hang from the leaning shoulders: turned back by a share of the sway they fall plumb.
                Quaternion back = Lean(-swayRoll * sway.ArmHang, -swayPitch * sway.ArmHang, forward, right);
                if (_leftArm != null)
                {
                    _leftArm.rotation = back * _leftArm.rotation;
                }

                if (_rightArm != null)
                {
                    _rightArm.rotation = back * _rightArm.rotation;
                }
            }
        }

        // Positive roll tips the top of the body to the right, positive pitch tips it forward.
        private static Quaternion Lean(float roll, float pitch, Vector3 forward, Vector3 right) =>
            Quaternion.AngleAxis(-roll, forward) * Quaternion.AngleAxis(pitch, right);

        // The angle a body leans to balance an acceleration, as a runner does in a turn.
        private static float LeanAngle(float acceleration, float scale, float max, CharacterRigConfig config) =>
            Mathf.Clamp(Mathf.Atan2(acceleration, config.LeanAcceleration) * Mathf.Rad2Deg * scale, -max, max);

        private Vector3 PlanarForward()
        {
            Vector3 forward = Vector3.ProjectOnPlane(_body.forward, _body.up);
            return forward.sqrMagnitude > 1e-6f ? forward.normalized : _body.forward;
        }
    }
}
