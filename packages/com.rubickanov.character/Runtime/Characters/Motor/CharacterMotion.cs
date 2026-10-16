using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>Snapshot after collision resolution. Local velocity excludes transport by a platform.</summary>
    public readonly struct CharacterMotion
    {
        public readonly Vector3 Velocity;
        public readonly Vector3 LocalVelocity;
        /// <summary>Where the character looks in local space, pitch included. Zero when it just looks ahead.</summary>
        public readonly Vector3 Aim;
        public readonly Vector3 Up;
        public readonly bool IsGrounded;
        public readonly bool FoundGround;
        /// <summary>The character swims at the surface: neither grounded nor falling.</summary>
        public readonly bool IsSwimming;
        /// <summary>The body faces where it looks at once, every frame (first person): no turning in place.</summary>
        public readonly bool FacesLook;
        public float PlanarSpeed => new Vector2(LocalVelocity.x, LocalVelocity.z).magnitude;
        public float VerticalSpeed => LocalVelocity.y;

        public CharacterMotion(
            Vector3 velocity,
            Vector3 relativeVelocity,
            Vector3 aim,
            Quaternion rotation,
            bool isGrounded,
            bool foundGround,
            bool isSwimming = false,
            bool facesLook = false)
        {
            Quaternion toLocal = Quaternion.Inverse(rotation);
            Velocity = velocity;
            LocalVelocity = toLocal * relativeVelocity;
            Aim = toLocal * aim;
            Up = rotation * Vector3.up;
            IsGrounded = isGrounded;
            FoundGround = foundGround;
            IsSwimming = isSwimming;
            FacesLook = facesLook;
        }
    }
}
