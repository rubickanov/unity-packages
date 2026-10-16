using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// Water a character can swim in, such as the sea. The character does not know what the water is; the game
    /// sets it on <see cref="CharacterMotor.Water"/>, as snow is an <see cref="IFootGround"/>.
    /// </summary>
    public interface ICharacterWater
    {
        /// <summary>World height of the surface over <paramref name="point"/> and how the water there moves; false where there is no water.</summary>
        bool TrySample(Vector3 point, out float height, out Vector3 velocity);

        /// <summary>World height of the water at rest over <paramref name="point"/>, without its waves: how deep it is.</summary>
        float Level(Vector3 point);
    }
}
