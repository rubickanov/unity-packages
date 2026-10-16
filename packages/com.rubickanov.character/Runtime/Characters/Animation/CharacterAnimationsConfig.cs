using System;
using Animancer;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>Animations and timings for one kind of character. Can be shared by any number of characters.</summary>
    [CreateAssetMenu(menuName = "Characters/Animation Set")]
    public sealed class CharacterAnimationsConfig : ScriptableObject
    {
        [Header("Ground")]
        [Tooltip("Parameter is local planar velocity in m/s: X is right, Y is forward. " +
                 "Set thresholds to the speed each clip actually moves at.")]
        public MixerTransition2D Locomotion = new();
        [Tooltip("How quickly the mixer parameter follows velocity. 0 follows it instantly.")]
        [Min(0)] public float ParameterSharpness = 12f;

        [Header("Slopes")]
        [Tooltip("Optional. Forward locomotion uphill. Parameter is planar speed in m/s. " +
                 "Leave Extrapolate Speed off: Locomotion already scales the whole blend.")]
        public LinearMixerTransition Uphill = new();
        [Tooltip("Optional. Forward locomotion downhill, same parameter as Uphill.")]
        public LinearMixerTransition Downhill = new();
        [Tooltip("Ground angle the slope clips were authored for. Steeper slopes play them fully.")]
        [Range(1, 89)] public float SlopeClipAngle = 25f;
        [Tooltip("How quickly the slope blend follows the ground. 0 follows it instantly.")]
        [Min(0)] public float SlopeSharpness = 8f;

        [Header("Turning in place")]
        [Tooltip("Optional. Standing turns whose root rotation turns the model, picked by the nearest angle. " +
                 "Import them with root rotation not baked into the pose. Without any, the model simply follows the body.")]
        public TurnClip[] Turns = { };
        [Tooltip("How far where the character looks may stray from where the model faces before it turns in place.")]
        [Range(1, 180)] public float TurnAngle = 90f;
        [Tooltip("How long the look has to stay past Turn Angle. Hides turns when the camera only sweeps past.")]
        [Min(0)] public float TurnDelay = 0.2f;
        [Tooltip("Farthest the model lags behind the look while the body turns under it, before it is carried along.")]
        [Range(0, 180)] public float MaxTurnLag = 150f;
        [Tooltip("Planar speed in m/s above which the character is moving: turns give way and the model lines up with the body.")]
        [Min(0)] public float TurnMaxSpeed = 0.5f;
        [Tooltip("How quickly the model lines up with the body while moving. A jump on the spot keeps the model's turn.")]
        [Min(0)] public float TurnSettleSharpness = 10f;

        [Header("Air")]
        [Tooltip("Optional. Picked by planar speed in m/s before the jump; play once, then Fall takes over.")]
        public ThresholdClip[] Jumps = { };
        [Tooltip("Airborne loop.")]
        public ClipTransition Fall = new();
        [Tooltip("Optional. Airborne loop once falling fast.")]
        public ClipTransition FallLarge = new();
        [Tooltip("Downward speed in m/s at which Fall gives way to FallLarge.")]
        [Min(0)] public float LargeFallSpeed = 18f;
        [Tooltip("Airtime before walking off a ledge counts as falling. Hides tiny steps down.")]
        [Min(0)] public float FallDelay = 0.15f;

        [Header("Landing")]
        [Tooltip("Fastest downward speed in m/s during the fall below which touchdown goes straight to locomotion. " +
                 "Hides landings after tiny steps down.")]
        [Min(0)] public float MinLandFallSpeed = 6f;
        [Tooltip("Optional. Landing in place, picked by the fastest downward speed in m/s during the fall.")]
        public ThresholdClip[] StandingLands = { };
        [Tooltip("Optional. Landing on the move, picked by planar speed in m/s at takeoff.")]
        public ThresholdClip[] MovingLands = { };
        [Tooltip("Planar speed that separates standing and moving landings, and cuts either short " +
                 "when the character starts or stops moving.")]
        [Min(0)] public float LandCancelSpeed = 1f;
        [Tooltip("Fade from a landing that reached its end time back to locomotion. " +
                 "Set each landing's end time to where it has done its job, not to its last frame.")]
        [Min(0)] public float LandBlendOut = 0.3f;

        [Header("Getting up")]
        [Tooltip("Optional. Getting up from lying on the back, in place. Its first frame should lie flat: " +
                 "the ragdoll's pose blends into it. Without it the pose blends straight into standing.")]
        public ClipTransition GetUpFaceUp = new();
        [Tooltip("Optional. Getting up from lying on the front, in place.")]
        public ClipTransition GetUpFaceDown = new();
        [Tooltip("Fade from a get-up that reached its end time back to locomotion.")]
        [Min(0)] public float GetUpBlendOut = 0.25f;
        [Tooltip("Get-ups started from the frame nearest the lying body, its heading and place found by the search " +
                 "(clips that end facing the root's forward, root motion on the level travel). Empty: the face up / " +
                 "face down get-ups above. Needs GetUpPoses baked from this list.")]
        public ClipTransition[] GetUps = { };
        [Tooltip("Every frame of GetUps baked on the character's ragdoll (package editor GetUpBaker). A list " +
                 "changed without a rebake falls back to the face up / face down get-ups with a warning.")]
        public GetUpPoses GetUpPoses;

        [Header("Inertialization")]
        [Tooltip("A nearest-frame get-up starts its clip at once and the ragdoll's pose dies away over it " +
                 "(PoseInertia on the model) instead of a held clip blending in. Off: the held blend.")]
        public bool InertialGetUp;
        [Min(0)] public float GetUpInertia = 0.25f;
        [Tooltip("A jump starts with no cross-fade and the old pose dies away over it (PoseInertia).")]
        public bool InertialJump;
        [Tooltip("A landing, the same.")]
        public bool InertialLand;
        [Tooltip("A turn in place, the same.")]
        public bool InertialTurn;
        [Tooltip("Going into the water (swimming), the same.")]
        public bool InertialWaterEntry;
        [Tooltip("How long the old pose takes to die away on a jump, landing, turn or water entry, s.")]
        [Min(0)] public float SwitchInertia = 0.2f;

        [Header("Flailing")]
        [Tooltip("Flail clips a flying ragdoll's muscles reach for (RagdollConfig.Flail), in this order: head first, " +
                 "along the fall, feet first. Played in place and looped. Empty: no flailing.")]
        public ClipTransition[] Flails = { };

        [Header("Riding")]
        [Tooltip("Optional. Held while riding in or on something whose seat has no pose of its own. Without it " +
                 "such a rider idles.")]
        public ClipTransition Seated = new();

        [Header("Swimming")]
        [Tooltip("Optional. Swimming at the surface, as Locomotion: local planar velocity in m/s, the clip treading " +
                 "water at zero. Faster than the outermost clips plays them faster. Without it a swimmer falls.")]
        public MixerTransition2D Swim = new();
        [Tooltip("Optional. Played once when a swimmer lunges up out of the water.")]
        public ClipTransition SwimLunge = new();

        [Header("Holding")]
        [Tooltip("Optional. Both arms holding something in front of the chest, fingers closed: a one-frame pose " +
                 "played over each arm in place of the clip's swing while it holds (made by the builder).")]
        public AnimationClip HoldArms;
        [Tooltip("The right arm and its fingers, for HoldArms (made by the builder).")]
        public AvatarMask RightArmMask;
        [Tooltip("The left arm and its fingers, for HoldArms (made by the builder).")]
        public AvatarMask LeftArmMask;
        [Tooltip("Seconds an arm takes to go into the hold pose or back to the clip.")]
        [Min(0)] public float HoldFade = 0.2f;
        [Tooltip("Where HoldArms puts the upper chest in the model's frame (measured by the builder): a hold is set " +
                 "off the chest as it stands there, and goes with the chest's moves off it.")]
        public Vector3 HoldChestPosition;
        public Quaternion HoldChestRotation = Quaternion.identity;
        [Tooltip("The clavicles' local turns in HoldArms (measured by the builder).")]
        public Quaternion HoldShoulderRight = Quaternion.identity;
        public Quaternion HoldShoulderLeft = Quaternion.identity;

        /// <summary>The valid clip with the highest threshold at or below value, or one with no Animation.</summary>
        public static ThresholdClip Pick(ThresholdClip[] clips, float value)
        {
            ThresholdClip picked = default;
            float best = float.NegativeInfinity;
            foreach (ThresholdClip clip in clips)
            {
                if (clip.Animation != null && clip.Animation.IsValid
                    && clip.Threshold <= value && clip.Threshold >= best)
                {
                    picked = clip;
                    best = clip.Threshold;
                }
            }

            return picked;
        }

        /// <summary>The valid turn to the side of degrees whose angle is nearest to it, or null.</summary>
        public static ClipTransition PickTurn(TurnClip[] turns, float degrees, out float angle)
        {
            ClipTransition picked = null;
            angle = 0f;
            float best = float.PositiveInfinity;
            foreach (TurnClip turn in turns)
            {
                ClipTransition clip = degrees > 0f ? turn.Right : turn.Left;
                float distance = Mathf.Abs(Mathf.Abs(degrees) - turn.Angle);
                if (clip != null && clip.IsValid && turn.Angle > 0f && distance < best)
                {
                    picked = clip;
                    angle = turn.Angle;
                    best = distance;
                }
            }

            return picked;
        }
    }

    /// <summary>A standing turn to either side through the same angle.</summary>
    [Serializable]
    public struct TurnClip
    {
        [Tooltip("Degrees the clip's root turns.")]
        [Range(1, 180)] public float Angle;
        public ClipTransition Left;
        public ClipTransition Right;
    }

    /// <summary>An animation chosen once some value reaches its threshold.</summary>
    [Serializable]
    public struct ThresholdClip
    {
        public float Threshold;
        public ClipTransition Animation;
        [Tooltip("Optional. Planar speed in m/s the clip was animated at. Faster movement plays it faster " +
                 "so the feet do not slide.")]
        [Min(0)] public float AuthoredSpeed;
    }
}
