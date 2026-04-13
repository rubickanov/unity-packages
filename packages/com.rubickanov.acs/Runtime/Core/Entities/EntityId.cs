using System;
using System.Threading;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Session-local stable identifier for every <see cref="IEntity"/>. Allocated monotonically
    /// from a single static counter at entity construction, unique within a running process.
    /// <para/>
    /// Serves three roles: (1) routing key for future network messages that carry a reference
    /// to a specific entity, (2) anchor for cross-entity references stored inside aspect data,
    /// (3) lookup handle via <see cref="World.TryFindById"/>. It is NOT a persistence key —
    /// EntityId is re-allocated every session, so save/load must map a stable identifier
    /// (<c>acs.persistence</c> adds this on top) onto the live <see cref="EntityId"/>.
    /// <para/>
    /// The default value (<see cref="None"/>) represents "no entity" — useful as the absence
    /// of a reference in aspect fields. Non-None ids are always <c>&gt; 0</c>.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        /// <summary>
        /// Raw numeric value. 0 is reserved for <see cref="None"/> — any real entity has a value
        /// strictly greater than zero. Exposed so callers can serialize the id or use it as a
        /// dictionary key without going through the struct wrapper.
        /// </summary>
        public readonly ulong Value;

        /// <summary>
        /// Constructs an id from a raw numeric value. Primarily for tests that need to model
        /// collisions on the <see cref="EntityRegistry"/> by-id index, or for tooling that has
        /// already obtained a raw value (e.g. from a debug dump). Regular runtime code should
        /// never construct an id manually — every <see cref="IEntity"/> gets one allocated
        /// automatically at construction via the internal <see cref="Allocate"/> source.
        /// </summary>
        public EntityId(ulong value)
        {
            Value = value;
        }

        /// <summary>
        /// The "no entity" id — equivalent to <c>default(EntityId)</c>. Compare fields against
        /// this (or check <see cref="IsNone"/>) to express "this aspect currently references no
        /// entity". <see cref="World.TryFindById"/> returns false immediately for <see cref="None"/>.
        /// </summary>
        public static EntityId None => default;

        /// <summary>True iff this id is <see cref="None"/> (raw value 0).</summary>
        public bool IsNone => Value == 0;

        // Process-wide monotonic counter. Starts at 0; Interlocked.Increment returns 1 on the
        // first call, so every real id is ≥ 1 and the default(EntityId) slot stays reserved
        // for None. Stored as long because Interlocked.Increment has no ref ulong overload on
        // the runtimes Unity targets (.NET Standard 2.1 / Framework). A signed long exhausts at
        // ~9.2e18 — even at a million allocations per second it lasts ~292,000 years, so
        // wraparound is not a concern and the ulong surface stays sound.
        private static long _nextValue;

        /// <summary>
        /// Allocates the next unique id. Internal — callers don't invent ids, <see cref="IEntity"/>
        /// implementations request them at construction. Thread-safe via <see cref="Interlocked"/>.
        /// </summary>
        internal static EntityId Allocate() => new((ulong)Interlocked.Increment(ref _nextValue));

        public bool Equals(EntityId other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is EntityId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(EntityId left, EntityId right) => left.Value == right.Value;

        public static bool operator !=(EntityId left, EntityId right) => left.Value != right.Value;

        public override string ToString() => IsNone ? "Entity#None" : $"Entity#{Value}";
    }
}
