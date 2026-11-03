using Rubickanov.Save.Data;

namespace Rubickanov.Save.World
{
    /// <summary>A thing of the world that is saved: spawned back by its kind on load.</summary>
    public interface ISavePiece
    {
        SaveId SaveId { get; }

        /// <summary>A stable string from the game's catalog ("sunbed", a vehicle id).</summary>
        string Kind { get; }

        /// <summary>Writes the thing's own state, already calm; <c>$v</c> and <c>kind</c> are the section's.</summary>
        void Capture(SaveWriter w);
    }

    /// <summary>A piece that refers to other pieces: linked in a second pass, once every piece of the slot is spawned.</summary>
    public interface ISavePieceLinks
    {
        /// <summary>Reads the same record the factory read; <see cref="SavePieces.Resolve{T}"/> finds a piece by a SaveId field.</summary>
        void Link(SaveReader r, SavePieces pieces);
    }
}
