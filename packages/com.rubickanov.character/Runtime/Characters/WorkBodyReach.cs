using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>Reach by bending the spine, carrying shoulders with it. Hips, feet and all bone lengths stay intact.</summary>
    public sealed class WorkBodyReach
    {
        private readonly Transform _spine, _left, _right, _model;
        private readonly float _comfortReach;
        private Vector3 _bend;
        public float BendDegrees => _bend.magnitude;

        public WorkBodyReach(Animator avatar)
        {
            _model = avatar.transform;
            _spine = avatar.GetBoneTransform(HumanBodyBones.Spine);
            _left = avatar.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _right = avatar.GetBoneTransform(HumanBodyBones.RightUpperArm);
            float Length(HumanBodyBones a, HumanBodyBones b, HumanBodyBones c)
            {
                var upper = avatar.GetBoneTransform(a); var lower = avatar.GetBoneTransform(b); var hand = avatar.GetBoneTransform(c);
                return upper != null && lower != null && hand != null
                    ? Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position) : 0;
            }
            _comfortReach = .85f * Mathf.Min(Length(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
                Length(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand));
        }

        public void Apply(Vector3? target, float weight, float seconds, bool allowed)
        {
            if (_spine == null || _left == null || _right == null || _comfortReach <= 0) return;
            if (!allowed || weight <= 0) { _bend = Vector3.zero; return; }
            Vector3 wanted = target.HasValue ? DesiredBend(_spine.position, (_left.position + _right.position) * .5f,
                target.Value, _model.rotation, _comfortReach) : Vector3.zero;
            _bend = Vector3.Lerp(_bend, wanted, 1 - Mathf.Exp(-10 * Mathf.Max(0, seconds)));
            if (_bend.sqrMagnitude < .00001f) return;
            _spine.rotation = Quaternion.AngleAxis(_bend.magnitude * Mathf.Clamp01(weight), _model.rotation * _bend.normalized) * _spine.rotation;
        }

        public static Vector3 DesiredBend(Vector3 pivot, Vector3 shoulders, Vector3 target, Quaternion basis, float reach)
        {
            var direction = Quaternion.Inverse(basis) * (target - shoulders);
            if (!float.IsFinite(direction.x) || !float.IsFinite(direction.y) || !float.IsFinite(direction.z)
                || reach <= 0 || Vector3.Distance(shoulders, target) <= reach) return Vector3.zero;
            // Do not fold backwards toward a cursor behind the body. Side reaches combine roll and forward lean.
            float yaw = Mathf.Clamp(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, -70, 70);
            var forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            var axis = Vector3.Cross(Vector3.up, forward).normalized;
            float best = 0, error = Vector3.Distance(shoulders, target);
            for (int angle = 1; angle <= 42; angle++)
            {
                var moved = pivot + Quaternion.AngleAxis(angle, basis * axis) * (shoulders - pivot);
                float next = Vector3.Distance(moved, target);
                if (next < error) { error = next; best = angle; }
                if (next <= reach) break;
            }
            return axis * best;
        }
    }
}
