using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// Ground the feet stand on that physics does not know about, such as soft ground that holds a foot lower
    /// than its surface. <see cref="FootPlacement"/> takes the higher of it and what its ray hits; so do a lying body
    /// and the hands and feet getting up (<see cref="CharacterRig.BodyGround"/>).
    /// </summary>
    public interface IFootGround
    {
        /// <summary>World height of the ground a foot at <paramref name="point"/> stands on, and its normal; false if there is none here.</summary>
        bool TryGetGround(Vector3 point, out float height, out Vector3 normal);

        /// <summary>True for a collider the feet must not stand on at <paramref name="point"/>, because this ground replaces it there.</summary>
        bool Ignores(Collider collider, Vector3 point);
    }
}
