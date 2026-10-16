using UnityEngine;
using UnityEngine.Serialization;

namespace Rubickanov.Characters
{
    public enum CharacterUpMode { World, Gravity, GroundAndGravity }

    [CreateAssetMenu(menuName = "Characters/Movement Config")]
    public sealed class MovementConfig : ScriptableObject
    {
        [Header("Stable movement")]
        [Min(0)] public float MaxStableMoveSpeed = 10f;
        [Tooltip("Ground speed limit while sprinting.")]
        [Min(0)] public float MaxSprintSpeed = 14f;
        [Tooltip("How quickly ground speed rises toward the target.")]
        [FormerlySerializedAs("StableMovementSharpness")]
        [Min(0)] public float StableAccelerationSharpness = 6f;
        [Tooltip("How quickly ground speed falls toward a lower target, e.g. when stopping.")]
        [Min(0)] public float StableDecelerationSharpness = 4.5f;
        [Min(0)] public float OrientationSharpness = 10f;

        [Header("Air movement")]
        [Min(0)] public float MaxAirMoveSpeed = 15f;
        [Min(0)] public float AirAccelerationSpeed = 15f;
        [Min(0)] public float Drag = 0.1f;

        [Header("Jumping")]
        public bool AllowJumpingWhenSliding;
        [Min(0)] public float JumpUpSpeed = 10f;
        [Min(0)] public float JumpScalableForwardSpeed = 10f;
        [Min(0)] public float JumpPreGroundingGraceTime;
        [Min(0)] public float JumpPostGroundingGraceTime;

        [Header("Up alignment")]
        public CharacterUpMode UpMode;
        [Min(0)] public float UpOrientationSharpness = 10f;
        public Vector3 Gravity = new Vector3(0f, -30f, 0f);

        [Header("Swimming")]
        [Tooltip("How far below the surface the soles hang while swimming: the water reaches the shoulders.")]
        [Min(0)] public float FloatDepth = 0.97f;
        [Tooltip("Water this deep over the bottom makes a walker swim, once the soles are near the float line.")]
        [Min(0)] public float SwimEnterDepth = 1.02f;
        [Tooltip("Water shallower than this over the bottom puts a swimmer back on its feet.")]
        [Min(0)] public float SwimExitDepth = 0.82f;
        [Min(0)] public float SwimSpeed = 1.3f;
        [Min(0)] public float SwimSprintSpeed = 2.2f;
        [Min(0)] public float SwimAccelerationSharpness = 2.5f;
        [Min(0)] public float SwimDecelerationSharpness = 1.2f;
        [Tooltip("Share of the water's sideways motion that carries a swimmer.")]
        [Range(0, 1)] public float SwimDrift = 0.4f;
        [Tooltip("Stiffness of the spring that holds the body at the float line, 1/s². Higher rides the waves closer.")]
        [Min(0)] public float FloatSpring = 40f;
        [Tooltip("Damping of the float spring, 1/s, against the water's own rise and fall.")]
        [Min(0)] public float FloatDamping = 8f;
        [Tooltip("Upward speed of a lunge out of the water, m/s.")]
        [Min(0)] public float SwimJumpSpeed = 3.5f;
        [Min(0)] public float SwimJumpCooldown = 0.8f;
        [Tooltip("Highest edge above the shoulders (the water line while swimming) a swimmer pulls itself over, m. "
            + "A wave that lowers the edge or lifts the swimmer brings a higher one into reach.")]
        [Min(0)] public float ClimbReachUp = 0.6f;
    }

}
