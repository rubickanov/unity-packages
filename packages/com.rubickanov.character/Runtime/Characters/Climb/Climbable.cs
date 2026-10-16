using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>What a swimmer asks a climbable: where its hands are, where it faces, how high it pulls itself.</summary>
    public struct ClimbQuery
    {
        /// <summary>The swimmer's shoulders, at the water line while it swims.</summary>
        public Vector3 Grip;

        /// <summary>Level, unit: where the swimmer faces.</summary>
        public Vector3 Facing;

        /// <summary>Highest edge above <see cref="Grip"/> the swimmer can pull itself over, m.</summary>
        public float ReachUp;

        /// <summary>How far from the grip a single climb point may be, m.</summary>
        public float Reach;

        /// <summary>Cosine of the widest angle between the facing and a climb point's way onto it.</summary>
        public float FacingCos;

        public float Radius;
        public float Height;

        /// <summary>Layers that block a standing character.</summary>
        public int Layers;

        /// <summary>The climber's root: its own colliders never block.</summary>
        public Transform Self;
    }

    /// <summary>
    /// One way out of the water onto a climbable, in the space the climb runs in, so a ride that bobs or moves carries
    /// the climber along.
    /// </summary>
    public struct ClimbEdge
    {
        public IClimbable Target;
        public Transform Space;

        /// <summary>The top of the highest thing the climber pulls itself over.</summary>
        public Vector3 LocalRim;

        /// <summary>Where the soles end up.</summary>
        public Vector3 LocalTop;

        /// <summary>Level way onto the climbable: the climber's facing on top.</summary>
        public Vector3 LocalOnto;

        /// <summary>How high the rim is over the swimmer's grip, m (for prompts and logs).</summary>
        public float Height;

        /// <summary>From the grip to the rim on the level, m: the nearest edge wins.</summary>
        public float Distance;
    }

    /// <summary>Something a swimmer climbs out of the water onto: a tube's rim, a boat's side, a jet ski's stern.</summary>
    public interface IClimbable
    {
        /// <summary>The way up in front of the swimmer, if there is one it can reach now.</summary>
        bool TryEdge(in ClimbQuery query, out ClimbEdge edge);

        /// <summary>How fast a point of it moves: the climber lets go when it is jerked away.</summary>
        Vector3 VelocityAt(Vector3 point);

        /// <summary>
        /// The climber reached the top and stands there in control again, on the machine that simulates it (a
        /// <see cref="ClimbPoint"/> without a taker seats it instead).
        /// </summary>
        void Climbed(CharacterMotor climber);
    }

    /// <summary>Every climbable there is now.</summary>
    public static class Climbables
    {
        private static readonly List<IClimbable> Registry = new();

        public static IReadOnlyList<IClimbable> All => Registry;

        public static void Add(IClimbable climbable) => Registry.Add(climbable);

        public static void Remove(IClimbable climbable) => Registry.Remove(climbable);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();
    }
}
