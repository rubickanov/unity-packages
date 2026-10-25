using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// A pose held arms take in place of <see cref="CharacterAnimationsConfig.HoldArms"/> (a gun's stance): a humanoid
    /// clip played over the held arms, the clavicles' turns in it and the trunk's stance, each spine bone turned off
    /// the clip's own turn. The game's builder measures it on the character; see <see cref="CharacterAnimator.PoseArms"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Rubickanov/Characters/Arms Pose")]
    public sealed class ArmsPose : ScriptableObject
    {
        [Tooltip("Played over each held arm and its fingers, looped.")]
        public AnimationClip Clip;
        [Tooltip("The clavicles' local turns in the clip.")]
        public Quaternion ShoulderRight = Quaternion.identity;
        public Quaternion ShoulderLeft = Quaternion.identity;
        [Tooltip("Turns of the Spine, Chest and UpperChest bones on top of their local turns while the right arm holds: " +
                 "the stance's lean and twist.")]
        public Quaternion Spine = Quaternion.identity;
        public Quaternion Chest = Quaternion.identity;
        public Quaternion UpperChest = Quaternion.identity;
    }
}
