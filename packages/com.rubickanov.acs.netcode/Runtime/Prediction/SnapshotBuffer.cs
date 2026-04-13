using System;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Per-entity ring buffer of <c>[Replicated(Predicted = true)]</c> field snapshots keyed by tick.
    /// Feeds step-7 reconciliation on the owner client: after local <c>Simulate</c>
    /// runs at tick <c>T</c>, the owner captures the resulting predicted-field
    /// values into slot <c>T</c>; when authoritative state for <c>T</c> arrives
    /// via the replication layer, <see cref="PredictionManager{TInput}"/>
    /// replays inputs <c>T+1 .. currentTick</c> on top of the authoritative
    /// value — no snap-back on the owner side.
    /// </summary>
    /// <remarks>
    /// One <c>byte[Capacity * SlotSize]</c> backing array plus two small
    /// bookkeeping arrays — <see cref="BeginWrite"/> and <see cref="TryGet"/>
    /// hand out <see cref="Span{Byte}"/> slices into the backing array, so the
    /// ring is zero-alloc after construction. Non-generic because the
    /// predicted payload mixes heterogenous field types; the serialization
    /// format follows the existing replication wire order (alphabetical by
    /// field name, stable between peers). See
    /// <see cref="AspectReplicator.PredictedBindingIndices"/> for the layout
    /// source of truth.
    /// </remarks>
    [Preserve]
    internal sealed class SnapshotBuffer
    {
        internal const int Capacity = 64;

        private readonly byte[] _data;
        private readonly int[] _ticks;
        private readonly bool[] _valid;

        public int SlotSize { get; }

        private int _newestTick;
        private bool _anyWritten;

        public SnapshotBuffer(int slotSize)
        {
            SlotSize = slotSize;
            _data = new byte[Capacity * slotSize];
            _ticks = new int[Capacity];
            _valid = new bool[Capacity];
        }

        public bool HasAny => _anyWritten;
        public int NewestTick => _newestTick;

        // Lowest tick that could still be in the buffer. A tick below this is
        // guaranteed to have been overwritten by a newer generation — used by
        // reconcile to bail out rather than chase stale data.
        public int OldestTrackedTick => _anyWritten ? _newestTick - (Capacity - 1) : 0;

        /// <summary>
        /// Stamps the slot for <paramref name="tick"/> and hands back its
        /// backing span. Caller fills up to <see cref="SlotSize"/> bytes.
        /// The span is array-backed, safe to hold across calls within the frame.
        /// </summary>
        public Span<byte> BeginWrite(int tick)
        {
            int idx = Mod(tick, Capacity);
            _ticks[idx] = tick;
            _valid[idx] = true;
            if (!_anyWritten || tick > _newestTick)
            {
                _newestTick = tick;
                _anyWritten = true;
            }
            return _data.AsSpan(idx * SlotSize, SlotSize);
        }

        /// <summary>
        /// Returns the snapshot bytes for <paramref name="tick"/> if the slot
        /// still holds that exact tick (strict match — wrap-around collisions
        /// report as a miss).
        /// </summary>
        public bool TryGet(int tick, out Span<byte> data)
        {
            int idx = Mod(tick, Capacity);
            if (_valid[idx] && _ticks[idx] == tick)
            {
                data = _data.AsSpan(idx * SlotSize, SlotSize);
                return true;
            }
            data = default;
            return false;
        }

        private static int Mod(int tick, int capacity)
        {
            int r = tick % capacity;
            return r < 0 ? r + capacity : r;
        }
    }
}
