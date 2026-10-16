using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>One hand's positional reach. Release keeps the last point while its influence fades.</summary>
    public sealed class WorkHandReach
    {
        private float _reach;
        public Vector3 Position { get; private set; }
        public float Weight { get; private set; }

        public void Step(Transform target, float requestedWeight, float rigWeight, bool allowed, float seconds)
        {
            if (!allowed) { Reset(); return; }
            bool valid = target != null && target.gameObject.activeInHierarchy && Finite(target.position);
            if (valid) Position = target.position;
            float wanted = valid && float.IsFinite(requestedWeight) ? Mathf.Clamp01(requestedWeight) : 0;
            float dt = float.IsFinite(seconds) ? Mathf.Max(0, seconds) : 0;
            _reach = Mathf.MoveTowards(_reach, wanted, dt * 6f);
            Weight = _reach * (float.IsFinite(rigWeight) ? Mathf.Clamp01(rigWeight) : 0);
        }

        public void Reset() { _reach = 0; Weight = 0; }
        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
