using UnityEngine;

namespace Rubickanov.Characters.Netcode
{
    /// <summary>
    /// A game's own data riding in its character's snapshots: a component of the game on the character (any child).
    /// Its bytes play back at the time of the snapshot they came in, like a jump.
    /// </summary>
    public interface ICharacterNetPart
    {
        /// <summary>Unique among the parts of one character; the game chooses it.</summary>
        byte PartId { get; }

        /// <summary>
        /// Owner, asked on every simulation step, never short-circuited: true sends this step at once instead of at
        /// the next tick, for a change whose timing matters. The part may update its own state here.
        /// </summary>
        bool SendNow { get; }

        /// <summary>
        /// Owner, on every snapshot that goes out. Writing nothing leaves the part out of this snapshot; the part still
        /// counts it as sent.
        /// </summary>
        void Write(ref CharacterNetPartWriter writer);

        /// <summary>Replica, when playback reaches a snapshot that has this part's bytes, in order.</summary>
        void Play(CharacterNetPartReader reader);
    }

    /// <summary>
    /// A part that gives the space the pose is sent in, e.g. a moving deck the character stands on. At most one per
    /// character.
    /// </summary>
    public interface ICharacterNetFrame : ICharacterNetPart
    {
        /// <summary>Owner, at send, before <see cref="ICharacterNetPart.Write"/>: false keeps the pose in the world.</summary>
        bool ToFrame(ref Vector3 position, ref Quaternion rotation);

        /// <summary>Replica: this machine's copy of the space the part's bytes name, or null for the world.</summary>
        ICharacterSpace Space(CharacterNetPartReader reader);
    }
}
