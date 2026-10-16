using Animancer;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// A place a character rides in or on, such as a seat of a sled: what the body, its animation and its rig need to
    /// know about it. The character does not know what carries it; the seat implements this, as snow does
    /// <see cref="IFootGround"/>. Set on <see cref="CharacterMotor.Mount"/> on every machine.
    /// </summary>
    public interface ICharacterMount
    {
        /// <summary>
        /// Where the rider's body goes and which way it faces: the character's root, the bottom of its capsule, where
        /// the pose has its feet. The pose puts the hips above it, so the anchor sits on the seat, not at hip height.
        /// </summary>
        Transform Anchor { get; }

        /// <summary>The pose to hold. Null or invalid: the default seated pose of the character's animation set.</summary>
        ClipTransition Pose { get; }

        /// <summary>Where the left hand reaches. Each limb target may be null: the limb then stays as the clip has it.</summary>
        Transform LeftHand { get; }
        Transform RightHand { get; }
        Transform LeftFoot { get; }
        Transform RightFoot { get; }

        /// <summary>How each hand closes on its hold.</summary>
        HandShape LeftHandShape => HandShape.Fist;
        HandShape RightHandShape => HandShape.Fist;

        /// <summary>The rider's hands are free to work while seated, such as throwing snowballs or digging.</summary>
        bool HandsFree { get; }

        /// <summary>How far the camera stays from the rider, in metres. 0: the character's own distance.</summary>
        float CameraDistance { get; }

        /// <summary>
        /// Velocity of the anchor in the world, m/s, on every machine: what a rider throws from and stands up with. The
        /// motor of a seated character is suspended, so its own velocity says nothing of the ride.
        /// </summary>
        Vector3 Velocity { get; }
    }
}
