using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>How a character's ragdoll falls, lies and gets up. Can be shared by any number of characters.</summary>
    [CreateAssetMenu(menuName = "Characters/Ragdoll")]
    public sealed class RagdollConfig : ScriptableObject
    {
        [Header("Muscles")]
        [Tooltip("Joint drive towards the pose the character fell in, at full muscle weight, N·m per rad. " +
                 "A torque: a drive that asks an acceleration converges far slower once every joint moves.")]
        [Min(0)] public float Spring = 400f;
        [Tooltip("Joint damping at full muscle weight, N·m per rad/s.")]
        [Min(0)] public float Damper = 40f;
        [Tooltip("The muscle weight a fallen body fades to. Above 0 the limbs keep a little tone.")]
        [Range(0, 1)] public float LimpMuscleWeight = 0.01f;
        [Tooltip("Seconds the muscles take to fade from full to limp: the body tries to hold itself, then gives in.")]
        [Min(0)] public float LimpTime = 1f;
        [Tooltip("Damping added to the joints while limp, N·m per rad/s.")]
        [Min(0)] public float LimpDamper = 2f;

        [Header("Falling")]
        [Tooltip("The fastest a bone is sent off when the ragdoll takes over, m/s.")]
        [Min(0)] public float MaxActivationSpeed = 10f;
        [Tooltip("The fastest a body is thrown off something moving, e.g. a vehicle, m/s.")]
        [Min(0)] public float MaxThrowSpeed = 25f;
        [Tooltip("A character falling this long, seconds from the top of its flight, goes down into its ragdoll in " +
                 "the air at its full speed, and the bones fall with the character's gravity until they reach the " +
                 "ground. 0: never (the default; a game turns it on in its config).")]
        [Min(0)] public float FallTime;
        [Tooltip("The fastest a push sends the body off, m/s at the hips.")]
        [Min(0)] public float MaxPushSpeed = 12f;
        [Tooltip("The fastest a push sends the bone it lands on, m/s over the whole body. The rest goes to the " +
                 "whole body, so a light limb is not torn off its joint.")]
        [Min(0)] public float MaxHitBoneSpeed = 6f;
        [Tooltip("How many joints from the bone it lands on a push spreads.")]
        [Range(0, 4)] public int PushReach = 2;
        [Tooltip("A push's change of speed on a bone, times this for every joint it is from the bone it lands on.")]
        [Range(0, 1)] public float PushFalloff = 0.5f;
        [Tooltip("Solver position iterations per bone. Rigidbodies do not save this, so it is set on every fall.")]
        [Range(1, 32)] public int SolverIterations = 12;
        [Tooltip("Solver velocity iterations per bone, set on every fall. 1 is the physics default.")]
        [Range(1, 32)] public int SolverVelocityIterations = 1;
        [Tooltip("Frictionless while the muscles hold, so limbs do not snag on the way down.")]
        public PhysicsMaterial PinnedMaterial;
        [Tooltip("With friction once limp, so the body stops rather than slides.")]
        public PhysicsMaterial LimpMaterial;

        [Header("Lying")]
        [Tooltip("The least time the body lies on the ground or floats before it gets up, seconds; time in the " +
                 "air does not count.")]
        [Min(0)] public float MinDownTime = 1.5f;
        [Tooltip("The hips count as at rest below this speed, m/s.")]
        [Min(0)] public float RestSpeed = 0.2f;
        [Tooltip("How long the hips must be at rest before the character gets up, seconds.")]
        [Min(0)] public float RestTime = 0.5f;
        [Tooltip("Gets up after lying on the ground or floating this long however the body moves, seconds; time " +
                 "in the air does not count. 0 never forces it.")]
        [Min(0)] public float MaxDownTime = 8f;
        [Tooltip("How quickly the capsule catches up with the hips while down, seconds.")]
        [Min(0.01f)] public float FollowTime = 0.2f;

        [Header("Getting Up")]
        public LayerMask GroundLayers = 1;
        [Tooltip("How far below the hips the ground is looked for, metres.")]
        [Min(0)] public float GroundProbe = 1.5f;
        [Tooltip("The body lies on the ground while the hips are no higher above it than this, metres. It gets " +
                 "up by itself only then, never in the air or hung on something.")]
        [Min(0)] public float LyingHeight = 0.5f;
        [Tooltip("Seconds the pose blends from the ragdoll into the get-up clip.")]
        [Min(0)] public float GetUpBlendTime = 0.4f;
        [Tooltip("Without a get-up clip, seconds the pose blends straight into standing.")]
        [Min(0)] public float StandInBlendTime = 0.3f;

        [Header("Get-up spot")]
        [Tooltip("Before getting up, the capsule looks for the nearest place round where the body lies where it " +
                 "stands on stable ground (not a step's nose), out of walls, with room to stand and nothing fixed " +
                 "over the lying head; with none the body stays down and looks again. Off: it stands on the ground " +
                 "under the hips (the default; a game turns it on in its config).")]
        public bool SafeSpot;
        [Tooltip("Rings of points round the first spot are this far apart, metres.")]
        [Min(0.01f)] public float SpotStep = 0.06f;
        [Tooltip("How many rings of 8 points are tried.")]
        [Range(0, 8)] public int SpotRings = 3;
        [Tooltip("How many times a capsule put into something is moved out of it.")]
        [Range(0, 8)] public int SpotFreeTries = 4;
        [Tooltip("With no place to stand, the body looks again this often, seconds.")]
        [Min(0.02f)] public float SpotRetry = 0.5f;
        [Tooltip("Down this long, seconds from the fall, it gets up where it lies, place or not.")]
        [Min(0)] public float SpotGiveUp = 8f;
        [Tooltip("The standing capsule and the head may be this deep in a fixed piece, metres.")]
        [Min(0)] public float PieceSlop = 0.02f;

        [Header("Water")]
        [Tooltip("Lift of a bone fully under water as a share of its weight. Above 1 floats.")]
        [Min(0)] public float Buoyancy = 1.25f;
        [Tooltip("Lift of the chest under water, the life vest: floats the body face up.")]
        [Min(0)] public float VestBuoyancy = 4f;
        [Tooltip("How far in front of the chest the vest lifts, metres; turns a body lying face down over.")]
        [Min(0)] public float VestForward = 0.12f;
        [Tooltip("Depth over which a bone goes from dry to fully under water, metres.")]
        [Min(0.01f)] public float ImmersionDepth = 0.2f;
        [Tooltip("Drag of the water on a bone moving through it, 1/s.")]
        [Min(0)] public float WaterDrag = 3f;
        [Min(0)] public float WaterAngularDrag = 2f;
        [Tooltip("The hips count as at rest below this speed through the water, m/s.")]
        [Min(0)] public float WaterRestSpeed = 0.5f;
        [Tooltip("Seconds the pose blends from the ragdoll into swimming.")]
        [Min(0)] public float SwimUpBlendTime = 0.5f;
    }
}
