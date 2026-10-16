using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// How hard a ragdoll bone presses on anything that is not the character's own, for the flail's touch. Added to
    /// the bones by <see cref="Ragdoll"/> only when the flail is on, so a game without it gets no collision callbacks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RagdollTouch : MonoBehaviour
    {
        private static readonly ProfilerMarker TouchMarker = new("RagdollTouch");

        private HashSet<Collider> _own;
        private float _force;

        internal void Construct(HashSet<Collider> own) => _own = own;

        /// <summary>The force pressed since the last take, N, and starts again from 0.</summary>
        public float Take()
        {
            float force = _force;
            _force = 0f;
            return force;
        }

        private void OnCollisionEnter(Collision collision) => Add(collision);

        private void OnCollisionStay(Collision collision) => Add(collision);

        private void Add(Collision collision)
        {
            using ProfilerMarker.AutoScope scope = TouchMarker.Auto();
            if (_own != null && _own.Contains(collision.collider))
            {
                return;
            }

            _force += collision.impulse.magnitude / Time.fixedDeltaTime;
        }
    }
}
