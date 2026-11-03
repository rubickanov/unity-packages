using System;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>What hit a bone of a shell (<see cref="CharacterRagdoll.Shell"/>), as the physics gave it to the bone.</summary>
    public readonly struct ShellHit
    {
        /// <summary>The shell's bone that was hit.</summary>
        public readonly RagdollBone Bone;

        /// <summary>The collider that hit it, a bone of another ragdoll as the game's layers have it.</summary>
        public readonly Collider Other;

        /// <summary>The first contact point, in the world.</summary>
        public readonly Vector3 Point;

        /// <summary>The contact normal, out of the other body into the shell's bone.</summary>
        public readonly Vector3 Normal;

        /// <summary>The two bodies' relative velocity at the hit, m/s, as <see cref="Collision.relativeVelocity"/>.</summary>
        public readonly Vector3 RelativeVelocity;

        /// <summary>The impulse the hit took in its step, N·s, as <see cref="Collision.impulse"/>.</summary>
        public readonly Vector3 Impulse;

        public ShellHit(RagdollBone bone, Collider other, Vector3 point, Vector3 normal, Vector3 relativeVelocity,
            Vector3 impulse)
        {
            Bone = bone;
            Other = other;
            Point = point;
            Normal = normal;
            RelativeVelocity = relativeVelocity;
            Impulse = impulse;
        }
    }

    /// <summary>
    /// Reports what hits a shell's bone. Added to the bones by <see cref="Ragdoll.Shell"/> and destroyed when the shell
    /// ends, so a body that is not a shell gets no collision callbacks from it.
    /// </summary>
    public sealed class ShellContact : MonoBehaviour
    {
        private RagdollBone _bone;
        private Action<ShellHit> _hit;

        internal void Construct(RagdollBone bone, Action<ShellHit> hit)
        {
            _bone = bone;
            _hit = hit;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_hit == null || collision.contactCount == 0)
            {
                return;
            }

            ContactPoint contact = collision.GetContact(0);
            _hit(new ShellHit(_bone, collision.collider, contact.point, contact.normal, collision.relativeVelocity,
                collision.impulse));
        }
    }
}
