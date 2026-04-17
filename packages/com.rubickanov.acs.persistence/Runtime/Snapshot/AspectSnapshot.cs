using System.Collections.Generic;

namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Serializable, detachable bag of aspect state. Keyed by the stable snapshot key —
    /// <see cref="PersistedKeyAttribute"/> when present on the aspect type,
    /// <see cref="System.Type.FullName"/> otherwise. The object survives a trip through
    /// any serializer the save layer chooses (JsonUtility, Newtonsoft, MsgPack, binary,
    /// etc.). ACS itself never writes this to disk — the save layer owns the format,
    /// the file, the slot, and the timing.
    /// <para/>
    /// Iteration order of <see cref="Aspects"/> is ordinal-sorted by key. This is a
    /// hard guarantee from the underlying <see cref="SortedDictionary{TKey,TValue}"/>
    /// using <see cref="System.StringComparer.Ordinal"/> — identical state produces
    /// identical iteration order across runtimes and cultures, which matters for
    /// autosave deduplication and byte-wise save-file equality.
    /// </summary>
    public sealed class AspectSnapshot
    {
        /// <summary>
        /// Aspects captured in this snapshot, keyed by the stable snapshot key —
        /// <see cref="PersistedKeyAttribute"/> when present on the aspect type,
        /// <see cref="System.Type.FullName"/> otherwise. Aspects with no
        /// <c>[PersistedState]</c> fields are omitted from the map. Iteration is
        /// ordinal-sorted.
        /// </summary>
        public SortedDictionary<string, AspectData> Aspects { get; }

        /// <summary>Creates an empty snapshot. Used by <c>Snapshot()</c> before filling.</summary>
        public AspectSnapshot()
        {
            Aspects = new SortedDictionary<string, AspectData>(System.StringComparer.Ordinal);
        }

        /// <summary>
        /// Wraps an externally-constructed aspect map — typical after a save-layer
        /// deserializer hands the dictionary back for <c>Restore()</c>. Entries are
        /// copied into an ordinal-sorted map, so the caller's collection type does
        /// not affect determinism.
        /// </summary>
        public AspectSnapshot(IDictionary<string, AspectData> aspects)
        {
            Aspects = aspects == null
                ? new SortedDictionary<string, AspectData>(System.StringComparer.Ordinal)
                : new SortedDictionary<string, AspectData>(aspects, System.StringComparer.Ordinal);
        }

        /// <summary>True when no aspect entries were captured.</summary>
        public bool IsEmpty => Aspects.Count == 0;
    }
}
