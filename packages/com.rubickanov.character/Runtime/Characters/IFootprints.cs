using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// Told each time a foot is set down on the ground, for marks it leaves, such as prints in sand.
    /// <see cref="FootPlacement"/> calls it on every machine that shows the character.
    /// </summary>
    public interface IFootprints
    {
        /// <summary>A foot set down: the ground under its ankle and under its ball (world).</summary>
        void Planted(Vector3 ankle, Vector3 ball, bool left);
    }
}
