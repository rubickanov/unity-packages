using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// The character's build: everything size-dependent (skeleton, primitives, capsule, eye height, ragdoll
    /// capsules and masses) is derived from it by the character builder. Shares are of the full height unless
    /// stated otherwise.
    /// </summary>
    [CreateAssetMenu(menuName = "Characters/Proportions", fileName = "CharacterProportions")]
    public sealed class CharacterProportions : ScriptableObject
    {
        [Header("Body")][Min(0.3f)] public float Height = 1.3f;

        /// <summary>Head height (the sphere's diameter).</summary>
        [Range(0.1f, 0.4f)] public float Head = 0.25f;

        /// <summary>Height of the hip joints above the soles.</summary>
        [Range(0.2f, 0.6f)] public float Legs = 0.35f;

        [Range(0f, 0.1f)] public float Neck = 0.03f;

        /// <summary>Torso width; the shoulder joints sit on its sides.</summary>
        [Range(0.1f, 0.6f)] public float TorsoWidth = 0.34f;

        [Range(0.1f, 0.5f)] public float TorsoDepth = 0.24f;

        /// <summary>Distance between the hip joints, as a share of the torso width.</summary>
        [Range(0.2f, 1f)] public float HipSpan = 0.5f;

        /// <summary>Where hanging fingertips reach: 0 the hip joints, 1 the knees.</summary>
        [Range(0f, 1f)] public float ArmReach = 0.5f;

        /// <summary>Upper arm, forearm and hand as shares of the arm's length; normalised.</summary>
        public Vector3 ArmSegments = new(0.4f, 0.36f, 0.24f);

        [Range(0.01f, 0.1f)] public float ArmRadius = 0.035f;
        [Range(0.01f, 0.15f)] public float LegRadius = 0.05f;

        /// <summary>Ankle height above the sole.</summary>
        [Range(0.02f, 0.15f)] public float FootHeight = 0.05f;

        [Range(0.05f, 0.3f)] public float FootLength = 0.16f;
        [Range(0.02f, 0.15f)] public float FootWidth = 0.08f;

        /// <summary>Whole-body mass, shared among the ragdoll's bones.</summary>
        [Min(1f)] public float Mass = 40f;

        /// <summary>Highest step the capsule climbs, as a share of the leg's length.</summary>
        [Header("Capsule and camera")]
        [Range(0f, 1f)] public float StepHeight = 0.55f;

        /// <summary>Eye level within the head, from its bottom (0) to its top (1).</summary>
        [Range(0f, 1f)] public float EyeLevel = 0.55f;

        [Header("Look")]
        public Color Skin = new(0.96f, 0.76f, 0.6f);
        public Color Shirt = new(0.95f, 0.45f, 0.2f);
        public Color Shorts = new(0.15f, 0.35f, 0.75f);
    }
}
