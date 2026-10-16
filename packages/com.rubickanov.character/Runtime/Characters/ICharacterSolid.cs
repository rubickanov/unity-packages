namespace Rubickanov.Characters
{
    /// <summary>
    /// A dynamic body a character walks into instead of through, on the component of its rigidbody, such as a big snow
    /// ball: the capsule stops at it and goes no faster than it moves, and the body is pushed by its own rules, not by the
    /// capsule. Other dynamic bodies (sleds, ragdolls, lumps) are passed through and pushed aside by physics, as before.
    /// </summary>
    public interface ICharacterSolid
    {
        /// <summary>False while it is too small to stop a character (a small ball is kicked, not leaned on).</summary>
        bool StopsCharacters { get; }
    }
}
