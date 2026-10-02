using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Rubickanov.Save
{
    /// <summary>
    /// Everything the game keeps of the player's, by what it is (<see cref="SaveArea"/>) and its name there, never by
    /// a path: a port to another platform swaps this store and nothing else. Every call may finish later, as console
    /// saves do; a store over a quick disk may finish at once. A write is whole or not at all: a crash while writing
    /// leaves what was there before. A platform that cannot do it throws <see cref="SaveStoreException"/>. Any thread
    /// may call it, but <see cref="ReadPictureAsync"/> only the main one. One per app.
    /// </summary>
    public interface ISaveStore
    {
        /// <summary>The whole of <paramref name="key"/>; null when there is none.</summary>
        UniTask<byte[]> ReadAsync(SaveKey key, CancellationToken ct = default);

        /// <summary>
        /// The first <paramref name="count"/> bytes of <paramref name="key"/>, fewer when it is shorter, without
        /// reading the rest where the platform can; null when there is none.
        /// </summary>
        UniTask<byte[]> ReadStartAsync(SaveKey key, int count, CancellationToken ct = default);

        /// <summary>Keeps <paramref name="bytes"/> as <paramref name="key"/>, in place of what it was, whole or not at all.</summary>
        UniTask WriteAsync(SaveKey key, byte[] bytes, CancellationToken ct = default);

        /// <summary>Forgets <paramref name="key"/>; false when there was none.</summary>
        UniTask<bool> DeleteAsync(SaveKey key, CancellationToken ct = default);

        /// <summary><paramref name="key"/>'s size and write time, without reading it; null when there is none.</summary>
        UniTask<SaveEntry?> FindAsync(SaveKey key, CancellationToken ct = default);

        /// <summary>Every entry of <paramref name="area"/> whose name starts with <paramref name="prefix"/>, by name.</summary>
        UniTask<IReadOnlyList<SaveEntry>> ListAsync(SaveArea area, string prefix = "", CancellationToken ct = default);

        /// <summary>
        /// <paramref name="key"/> decoded as a picture (PNG or JPEG) off the main thread where the platform can, mipped,
        /// sRGB, clamped, trilinear and anisotropic; null when there is none or it is not a picture. The caller owns the
        /// texture. It is the store's, as each platform has its own way to decode off the main thread.
        /// </summary>
        UniTask<Texture2D> ReadPictureAsync(SaveKey key, CancellationToken ct = default);

        /// <summary>Where <paramref name="key"/> is kept, for logs and the console: a file's path on desktop.</summary>
        string Describe(SaveKey key);
    }
}
