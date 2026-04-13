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
    /// Slots are raw <c>byte[]</c> sized to the entity's predicted payload, so
    /// Capture/Restore is zero-alloc once the buffer is constructed. Non-generic
    /// because the predicted payload mixes heterogenous field types; the
    /// serialization format follows the existing replication wire order
    /// (alphabetical by field name, stable between peers). See
    /// <see cref="AspectReplicator.PredictedBindingIndices"/> for the layout
    /// source of truth.
    /// </remarks>
    [Preserve]
    internal sealed class SnapshotBuffer
    {
        internal const int Capacity = 64;

        private struct Slot
        {
            public int Tick;
            public bool Valid;
            public byte[] Data;
        }

        private readonly Slot[] _slots;
        public int SlotSize { get; }

        private int _newestTick;
        private bool _anyWritten;

        public SnapshotBuffer(int slotSize)
        {
            SlotSize = slotSize;
            _slots = new Slot[Capacity];
            for (int i = 0; i < Capacity; i++)
                _slots[i].Data = new byte[slotSize];
        }

        public bool HasAny => _anyWritten;
        public int NewestTick => _newestTick;

        // Lowest tick that could still be in the buffer. A tick below this is
        // guaranteed to have been overwritten by a newer generation — used by
        // reconcile to bail out rather than chase stale data.
        public int OldestTrackedTick => _anyWritten ? _newestTick - (Capacity - 1) : 0;

        /// <summary>
        /// Stamps the slot for <paramref name="tick"/> and hands back its
        /// backing byte[]. Caller fills up to <see cref="SlotSize"/> bytes.
        /// </summary>
        public byte[] BeginWrite(int tick)
        {
            int idx = Mod(tick, Capacity);
            _slots[idx].Tick = tick;
            _slots[idx].Valid = true;
            if (!_anyWritten || tick > _newestTick)
            {
                _newestTick = tick;
                _anyWritten = true;
            }
            return _slots[idx].Data;
        }

        /// <summary>
        /// Returns the snapshot bytes for <paramref name="tick"/> if the slot
        /// still holds that exact tick (strict match — wrap-around collisions
        /// report as a miss).
        /// </summary>
        public bool TryGet(int tick, out byte[] data)
        {
            int idx = Mod(tick, Capacity);
            if (_slots[idx].Valid && _slots[idx].Tick == tick)
            {
                data = _slots[idx].Data;
                return true;
            }
            data = null!;
            return false;
        }

        private static int Mod(int tick, int capacity)
        {
            int r = tick % capacity;
            return r < 0 ? r + capacity : r;
        }
    }
}
