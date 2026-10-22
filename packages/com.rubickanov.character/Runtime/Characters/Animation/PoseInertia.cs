using Rubickanov.Rig;
using Unity.Profiling;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// Inertialization over Animancer: on a switch of clip the offset from the pose shown before to the new one is
    /// laid over the new pose and dies away (<see cref="Inertia"/>), keeping the old pose's velocity, with no
    /// overshoot. Runs after Animancer (<see cref="CharacterAnimator"/>) and before the IK (CharacterRig) on ~22 body
    /// bones (no fingers, eyes or jaw) and the hips' place in the model's space. Optional: a character without it
    /// cross-fades as before. It reads the shown pose every frame only while a switch kind that needs the last frame
    /// (<see cref="Tracking"/>) is on; a get-up hands its start pose in itself (<see cref="BeginFrom"/>).
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public sealed class PoseInertia : MonoBehaviour
    {
        private static readonly HumanBodyBones[] BodyBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
            HumanBodyBones.RightToes,
        };

        private static readonly ProfilerMarker StepMarker = new("PoseInertia.Step");

        [Tooltip("The Animator whose humanoid bones it moves; its transform is the model's space.")]
        public Animator Animator;

        private Transform _model;
        private Transform[] _bones;
        private int _count;

        // Last frame's shown pose (bone 0, the hips, in the model's space; the rest local) and its velocity.
        private Quaternion[] _last;
        private Vector3[] _lastSpin;
        private Vector3 _lastHips;
        private Vector3 _lastHipsVelocity;
        private bool _hasLast;
        private float _lastTime;

        // The new pose's velocity for the next begin, when the caller knows it (a clip evaluated a step before).
        private Quaternion[] _targetBefore;
        private Vector3 _targetHipsBefore;
        private float _targetStep;

        // The offset laid on: per bone a turn offset and rate, the hips' place offset and rate.
        private Vector3[] _x;
        private Vector3[] _v;
        private Vector3 _hipsX;
        private Vector3 _hipsV;
        private float _start;
        private float _length;

        // A switch begun without the new pose's velocity: what it began from, so the next frame, which shows where
        // the new pose went, sets the rates again (the first frame shows the offset alone, whatever its rate).
        private bool _refine;
        private Quaternion[] _fromLast;
        private Vector3[] _fromSpin;
        private Quaternion[] _fromTarget;
        private Vector3 _fromHips;
        private Vector3 _fromHipsVelocity;
        private Vector3 _fromTargetHips;
        private float _fromDt;
        private bool _active;
        private float _pendingLength;

        // Scratch for a begin.
        private Quaternion[] _now;

        /// <summary>Read the shown pose every frame, for switches begun from it (jump, land, turn, water entry).</summary>
        public bool Tracking { get; set; }

        /// <summary>
        /// The Animator left the bones as drawn this frame (<see cref="CharacterAnimator.Culled"/>): nothing is laid
        /// over them and the pose is read afresh once it writes again.
        /// </summary>
        public bool Paused { get; set; }

        /// <summary>An offset is being laid over the pose.</summary>
        public bool Active => _active;

        /// <summary>The bones it moves, the hips first.</summary>
        public int BoneCount => Bind() ? _count : 0;

        private void OnDisable()
        {
            _active = false;
            _hasLast = false;
            _pendingLength = 0f;
        }

        /// <summary>A clip was switched to this frame: the offset from the last shown pose begins once Animancer has
        /// evaluated, over <paramref name="length"/> s.</summary>
        public void Request(float length)
        {
            if (length > 0f && isActiveAndEnabled)
            {
                _pendingLength = length;
            }
        }

        private void LateUpdate()
        {
            if (Paused)
            {
                OnDisable();
                return;
            }

            if (!_active && _pendingLength <= 0f && !Tracking)
            {
                _hasLast = false;
                return;
            }

            using ProfilerMarker.AutoScope scope = StepMarker.Auto();
            if (!Bind())
            {
                return;
            }

            if (_pendingLength > 0f)
            {
                if (_hasLast)
                {
                    Begin(_last, _lastSpin, _lastHips, _lastHipsVelocity, Time.time - _lastTime, _pendingLength);
                }

                _pendingLength = 0f;
            }
            else if (_refine)
            {
                Refine();
            }

            Apply();
            Record();
        }

        /// <summary>
        /// Holds what the clip evaluated now shows as the new pose <paramref name="dt"/> s before the start (negative:
        /// after it), so the next <see cref="BeginFrom"/> knows the new pose's velocity. Call with the clip that step
        /// off, then evaluate it at the start.
        /// </summary>
        public void TargetBefore(float dt)
        {
            if (!Bind())
            {
                return;
            }

            _targetBefore ??= new Quaternion[_count];
            Read(_targetBefore, out _targetHipsBefore);
            _targetStep = dt;
        }

        /// <summary>
        /// The pose shown now, to begin from after the body moves: <paramref name="locals"/> get the bones' local
        /// turns (the hips' in the world), <paramref name="hipsWorld"/> the hips' place in the world.
        /// </summary>
        public void Capture(Quaternion[] locals, out Vector3 hipsWorld)
        {
            if (!Bind())
            {
                hipsWorld = default;
                return;
            }

            for (int i = 1; i < _count; i++)
            {
                locals[i] = _bones[i].localRotation;
            }

            _bones[0].GetPositionAndRotation(out hipsWorld, out locals[0]);
        }

        /// <summary>
        /// Begins an offset from a pose taken by <see cref="Capture"/>, at rest, to the pose on the bones now (with
        /// the velocity <see cref="TargetBefore"/> gave), over <paramref name="length"/> s, and lays it on at once:
        /// the bones show the captured pose this frame.
        /// </summary>
        public void BeginFrom(Quaternion[] captured, Vector3 hipsWorld, float length)
        {
            if (!Bind())
            {
                return;
            }

            _last ??= new Quaternion[_count];
            _lastSpin ??= new Vector3[_count];
            Quaternion toModel = Quaternion.Inverse(_model.rotation);
            for (int i = 1; i < _count; i++)
            {
                _last[i] = captured[i];
                _lastSpin[i] = Vector3.zero;
            }

            _last[0] = toModel * captured[0];
            _lastSpin[0] = Vector3.zero;
            Vector3 hips = _model.InverseTransformPoint(hipsWorld);
            Begin(_last, _lastSpin, hips, Vector3.zero, 0f, length);
            Apply();
            if (Tracking)
            {
                Record();
            }
        }

        // The offsets from the last pose, moved on by dt at its velocity, to the pose on the bones now.
        private void Begin(Quaternion[] last, Vector3[] lastSpin, Vector3 lastHips, Vector3 lastHipsVelocity,
            float dt, float length)
        {
            _now ??= new Quaternion[_count];
            _x ??= new Vector3[_count];
            _v ??= new Vector3[_count];
            Read(_now, out Vector3 hips);
            bool knowsTarget = _targetStep != 0f && _targetBefore != null;
            for (int i = 0; i < _count; i++)
            {
                Vector3 targetSpin = knowsTarget ? Inertia.SpinBetween(_targetBefore[i], _now[i], _targetStep) : Vector3.zero;
                Inertia.BeginTurn(last[i], lastSpin[i], _now[i], targetSpin, dt, out _x[i], out _v[i]);
            }

            Vector3 targetVelocity = knowsTarget ? (hips - _targetHipsBefore) / _targetStep : Vector3.zero;
            Inertia.BeginPosition(lastHips, lastHipsVelocity, hips, targetVelocity, dt, out _hipsX, out _hipsV);
            _targetStep = 0f;
            _start = Time.time;
            _length = length;
            _active = true;
            _refine = !knowsTarget;
            if (_refine)
            {
                _fromLast ??= new Quaternion[_count];
                _fromSpin ??= new Vector3[_count];
                _fromTarget ??= new Quaternion[_count];
                System.Array.Copy(last, _fromLast, _count);
                System.Array.Copy(lastSpin, _fromSpin, _count);
                System.Array.Copy(_now, _fromTarget, _count);
                _fromHips = lastHips;
                _fromHipsVelocity = lastHipsVelocity;
                _fromTargetHips = hips;
                _fromDt = dt;
            }
        }

        // The begin again with the new pose's velocity, from where it went since (Sinew's Begin takes it with the
        // pose); the offset at the start is unchanged, so the frame already shown stays right.
        private void Refine()
        {
            _refine = false;
            float step = Time.time - _start;
            if (!_active || step <= 0f)
            {
                return;
            }

            Read(_now, out Vector3 hips);
            for (int i = 0; i < _count; i++)
            {
                Vector3 targetSpin = Inertia.SpinBetween(_fromTarget[i], _now[i], step);
                Inertia.BeginTurn(_fromLast[i], _fromSpin[i], _fromTarget[i], targetSpin, _fromDt, out _x[i], out _v[i]);
            }

            Inertia.BeginPosition(_fromHips, _fromHipsVelocity, _fromTargetHips, (hips - _fromTargetHips) / step, _fromDt,
                out _hipsX, out _hipsV);
        }

        private void Apply()
        {
            if (!_active)
            {
                return;
            }

            float t = Time.time - _start;
            if (t >= _length)
            {
                _active = false;
                return;
            }

            Quaternion modelRotation = _model.rotation;
            for (int i = 1; i < _count; i++)
            {
                _bones[i].localRotation = Inertia.ApplyTurn(_x[i], _v[i], _length, t, _bones[i].localRotation);
            }

            Quaternion hipsTurn = Inertia.ApplyTurn(_x[0], _v[0], _length, t,
                Quaternion.Inverse(modelRotation) * _bones[0].rotation);
            Vector3 hips = Inertia.ApplyPosition(_hipsX, _hipsV, _length, t,
                _model.InverseTransformPoint(_bones[0].position));
            _bones[0].SetPositionAndRotation(_model.TransformPoint(hips), modelRotation * hipsTurn);
        }

        // The shown pose and its velocity since the last read.
        private void Record()
        {
            _last ??= new Quaternion[_count];
            _lastSpin ??= new Vector3[_count];
            _now ??= new Quaternion[_count];
            Read(_now, out Vector3 hips);
            float dt = Time.time - _lastTime;
            for (int i = 0; i < _count; i++)
            {
                _lastSpin[i] = _hasLast ? Inertia.SpinBetween(_last[i], _now[i], dt) : Vector3.zero;
                _last[i] = _now[i];
            }

            _lastHipsVelocity = _hasLast && dt > 0f ? (hips - _lastHips) / dt : Vector3.zero;
            _lastHips = hips;
            _lastTime = Time.time;
            _hasLast = true;
        }

        // Bone 0 (the hips) in the model's space, the rest local.
        private void Read(Quaternion[] into, out Vector3 hips)
        {
            Quaternion toModel = Quaternion.Inverse(_model.rotation);
            into[0] = toModel * _bones[0].rotation;
            hips = _model.InverseTransformPoint(_bones[0].position);
            for (int i = 1; i < _count; i++)
            {
                into[i] = _bones[i].localRotation;
            }
        }

        private bool Bind()
        {
            if (_bones != null)
            {
                return _count > 0;
            }

            if (Animator == null || !Animator.isHuman)
            {
                _bones = new Transform[0];
                return false;
            }

            _model = Animator.transform;
            var bones = new Transform[BodyBones.Length];
            foreach (HumanBodyBones id in BodyBones)
            {
                Transform bone = Animator.GetBoneTransform(id);
                if (bone != null)
                {
                    bones[_count++] = bone;
                }
            }

            _bones = bones;
            return _count > 0 && bones[0] == Animator.GetBoneTransform(HumanBodyBones.Hips);
        }
    }
}
