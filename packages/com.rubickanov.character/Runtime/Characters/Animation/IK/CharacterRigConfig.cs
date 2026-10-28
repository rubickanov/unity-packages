using System;
using Rubickanov.Rig;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>Procedural touches on top of the animation. Can be shared by any number of characters.</summary>
    [CreateAssetMenu(menuName = "Characters/Rig")]
    public sealed class CharacterRigConfig : ScriptableObject
    {
        [Serializable]
        public struct LookBone
        {
            public HumanBodyBones Bone;
            [Tooltip("Share of the look yaw this bone turns. Shares along the chain should add up to about 1.")]
            [Range(0, 1)] public float Yaw;
            [Tooltip("Share of the look pitch this bone turns.")]
            [Range(0, 1)] public float Pitch;
            [Tooltip("Turns so the head points where the character looks, taking back what the animation " +
                     "does to it. Off, the bone adds its share on top of the animation.")]
            public bool Aims;
        }

        [Serializable]
        public struct LeanBone
        {
            public HumanBodyBones Bone;
            [Tooltip("Share of the lean this bone takes. Shares should add up to about 1.")]
            [Range(0, 1)] public float Share;
        }

        [Header("Feet")]
        public bool Feet = true;
        public LayerMask GroundLayers = ~0;
        [Tooltip("How far above the animated foot the ground probe starts. Covers steps up.")]
        [Min(0)] public float ProbeHeight = 0.5f;
        [Tooltip("Highest ground a foot rises to above the character's base.")]
        [Min(0)] public float MaxStepUp = 0.45f;
        [Tooltip("Lowest ground a foot reaches below the character's base; the pelvis drops to let it.")]
        [Min(0)] public float MaxStepDown = 0.45f;
        [Tooltip("A foot lifted this far above its planted height no longer tilts to the ground.")]
        [Min(0.01f)] public float PlantHeight = 0.12f;
        [Tooltip("Steepest the sole tilts to the ground. Ground steeper than this between the ankle and " +
                 "the ball counts as a step edge, and the foot stands level on its higher side.")]
        [Range(0, 60)] public float MaxFootAngle = 30f;
        public SpringSettings FootSpring = new(10f, 1f);
        public SpringSettings PelvisSpring = new(6f, 1f);
        [Tooltip("How quickly the feet let go on leaving the ground and take hold on landing.")]
        public SpringSettings FeetWeightSpring = new(4f, 1f);

        [Header("Getting up")]
        [Tooltip("Hands and feet stay out of the ground and where they touch down while getting up.")]
        public bool GetUpContacts = true;
        [Tooltip("How far above the ground the hand bone sits when the palm rests on it, metres.")]
        [Min(0)] public float HandClearance = 0.03f;
        [Tooltip("How far above the ground the toe bone sits when the foot stands on it, metres.")]
        [Min(0)] public float ToesClearance = 0.03f;
        public LimbPinSettings FootPin = new(0.04f, 0.04f, 0.2f, 0.25f, 0.08f, 0.15f);
        public LimbPinSettings HandPin = new(0.05f, 0.04f, 0.15f, 0.2f, 0.06f, 0.12f);
        [Tooltip("How quickly the contacts take hold as the get-up starts and let go after it.")]
        public SpringSettings ContactWeightSpring = new(4f, 1f);

        [Header("Riding")]
        [Tooltip("Hands and feet reach for the holds of the seat the character rides in, such as a sled's rope.")]
        public bool MountReach = true;
        [Tooltip("How long the hands and feet take to reach the seat's holds after sitting down, seconds.")]
        [Min(0)] public float MountReachTime = 0.2f;
        [Tooltip("Wrist: the wrist goes onto the hold and the hand keeps the clip's turn (a sled's rope). Palm: the " +
                 "hand takes the hold's turn exactly, the palm's middle on the hold facing its forward (Z) with the " +
                 "fingers along its up (Y), fingers shaped by the seat's hand shape.")]
        public MountGrip MountGrip = MountGrip.Wrist;
        [Tooltip("The palm's face lies this far off a hand hold, metres: the hold's forward is where the palm faces.")]
        [Min(0)] public float MountPalmDepth = 0.026f;
        [Tooltip("A hand on a hold bent further off the forearm than this, degrees, is worth a warning; the hand still " +
                 "takes the hold's turn.")]
        [Range(0, 90)] public float MountWristBend = 45f;
        [Tooltip("A hand on a hold twisted further about the forearm than this, degrees, is worth a warning; the hand " +
                 "still takes the hold's turn.")]
        [Range(0, 180)] public float MountWristTwist = 120f;

        [Tooltip("The way both palms face in their hand bones' space at rest, for a hand whose thumb grows out under " +
                 "the palm rather than off its edge. Zero: the palm faces away from the back of the hand, square to " +
                 "the fingers and the thumb's root.")]
        public Vector3 PalmFacing;

        [Header("Hand shapes")]
        [Tooltip("Fingers round a bar, handle or bottle.")]
        public HandShapePose Fist = new(65f, 40f, 10f);
        [Tooltip("Fingers flat on a big round thing, such as a ball.")]
        public HandShapePose OpenPalm = new(-5f, 0f, 30f);
        [Tooltip("Fingers round a gun's grip, the index finger hooked on the trigger, the thumb on the grip's far side.")]
        public HandShapePose Trigger = new(65f, 40f, 10f);

        [Header("Swimming")]
        [Tooltip("A swimmer's body rises by this much over the swimming clips, metres, so the chin clears the water " +
                 "treading it.")]
        [Min(0)] public float TreadLift = 0.08f;
        [Tooltip("Swimming forward at swimming speed, the body rises by this much more, metres: the crawling clips " +
                 "lie lower than the upright treading one. Slower takes a share.")]
        [Min(0)] public float CrawlLift = 0.18f;
        [Tooltip("Swimming forward at swimming speed, the body tilts nose up around the hips by this much, degrees, " +
                 "so the head comes out of the water and the legs sink. Slower takes a share.")]
        [Range(0, 60)] public float CrawlPitch = 26f;
        [Tooltip("How quickly the swimming pose follows the speed, and comes and goes with swimming, seconds.")]
        [Min(0)] public float SwimPoseTime = 0.15f;

        [Header("Look")]
        public bool Look = true;
        [Tooltip("Farthest the chain turns to either side. Past it, the head holds there until the legs turn the body.")]
        [Range(0, 180)] public float YawLimit = 75f;
        [Tooltip("Looking back within this many degrees of straight behind keeps the head over the shoulder " +
                 "it is on, rather than swinging it to the other.")]
        [Range(0, 90)] public float ShoulderHold = 45f;
        [Range(0, 90)] public float PitchUp = 45f;
        [Range(0, 90)] public float PitchDown = 60f;
        [Tooltip("1 points the head exactly where the character looks, whatever the animation does to it. " +
                 "0 adds the look on top of the animation, so a clip that tilts the head tilts the gaze too.")]
        [Range(0, 1)] public float HeadAim = 1f;
        [Tooltip("From the lowest spine bone to the head.")]
        public LookBone[] LookBones =
        {
            new() { Bone = HumanBodyBones.Spine, Yaw = 0.1f, Pitch = 0.3f },
            new() { Bone = HumanBodyBones.Chest, Yaw = 0.15f, Pitch = 0.3f },
            new() { Bone = HumanBodyBones.UpperChest, Yaw = 0.15f, Pitch = 0.2f },
            new() { Bone = HumanBodyBones.Neck, Yaw = 0.25f, Pitch = 0.1f, Aims = true },
            new() { Bone = HumanBodyBones.Head, Yaw = 0.35f, Pitch = 0.1f, Aims = true },
        };
        [Tooltip("Share of a first-person stride's turn of the hips (CharacterAnimationsConfig.StrideAngle) the spine "
                 + "takes back, so the chest faces the look; the neck and head aim the rest. Below the spine bones' "
                 + "own yaw shares it adds nothing.")]
        [Range(0, 1)] public float StrideTwist = 1f;
        [Tooltip("Spring of the lowest bone. Bones up the chain blend toward Head Spring, " +
                 "so the head leads and the body follows.")]
        public SpringSettings SpineSpring = new(2.5f, 0.75f);
        public SpringSettings HeadSpring = new(6f, 0.75f);

        [Header("Lean")]
        public bool Lean = true;
        [Tooltip("Scales the sideways lean into turns. 1 is physically plausible.")]
        [Min(0)] public float RollScale = 1f;
        [Range(0, 45)] public float MaxRoll = 12f;
        [Tooltip("Scales the lean forward when speeding up and back when slowing down.")]
        [Min(0)] public float PitchScale = 0.5f;
        [Range(0, 45)] public float MaxPitch = 6f;
        [Tooltip("Acceleration that leans the body 45 degrees. Lower leans more.")]
        [Min(0.01f)] public float LeanAcceleration = 9.81f;
        [Tooltip("Below 1 the body overshoots and sways back when a turn ends.")]
        public SpringSettings LeanSpring = new(2f, 0.45f);
        [Tooltip("How quickly the speed that drives the forward lean is smoothed.")]
        public SpringSettings SpeedSpring = new(3f, 1f);
        [Tooltip("From the hips up.")]
        public LeanBone[] LeanBones =
        {
            new() { Bone = HumanBodyBones.Hips, Share = 0.2f },
            new() { Bone = HumanBodyBones.Spine, Share = 0.3f },
            new() { Bone = HumanBodyBones.Chest, Share = 0.3f },
            new() { Bone = HumanBodyBones.UpperChest, Share = 0.2f },
        };
        [Tooltip("How far the pelvis slides into the turn, as a fraction of what leaning around the feet would give.")]
        [Range(0, 1)] public float HipShift = 0.5f;
        [Tooltip("How much of the roll the head takes back to keep the eyes level.")]
        [Range(0, 1)] public float HeadStabilize = 0.8f;

        public HandShapePose Shape(HandShape shape) => shape switch
        {
            HandShape.OpenPalm => OpenPalm,
            HandShape.Trigger => Trigger,
            _ => Fist,
        };
    }

    /// <summary>How a seated character's hands take the seat's hand holds.</summary>
    public enum MountGrip
    {
        Wrist,
        Palm,
    }
}
