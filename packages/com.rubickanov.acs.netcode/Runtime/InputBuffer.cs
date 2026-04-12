using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Fixed-capacity ring buffer of per-tick inputs keyed by the tick the input
    /// was gathered on. Replaces the single-slot hold-last behaviour the step-6
    /// prediction scaffolding used. Required on the owner side so
    /// <see cref="PredictionManager{TInput}.OnServerStateApplied"/> can replay
    /// <c>serverTick + 1 .. currentTick</c> after a reconcile; also useful on
    /// the server to run <c>Simulate</c> against the input tagged with the
    /// current tick instead of whichever input arrived last.
    /// </summary>
    /// <remarks>
    /// <para>Slots are indexed by <c>tick % Capacity</c>. <see cref="TryGet"/>
    /// matches strictly on <c>slot.Tick == tick</c> so a wrap-around collision
    /// (e.g. a reconcile that looks back further than <see cref="Capacity"/>
    /// ticks) is reported as a miss rather than a false positive. The server
    /// hold-last path uses <see cref="GetOrHoldLast"/> which walks backwards
    /// from <paramref>tick</paramref> to find the most recent valid earlier
    /// input — this is how we preserve the step-6 "hold last" semantics once
    /// we store every received tick in its own slot.</para>
    /// <para>Capacity 64 ≈ 2 s at 30 Hz, well past any loopback/LAN RTT the
    /// tests or a realistic session produce. Bump if a real target pushes
    /// beyond it.</para>
    /// </remarks>
    [Preserve]
    internal struct InputBuffer<TInput>
        where TInput : unmanaged, IInputCommand
    {
        internal const int Capacity = 64;

        private struct Slot
        {
            public int Tick;
            public TInput Input;
            public bool Valid;
        }

        private Slot[] _slots;
        // Newest stored tick across the ring. GetOrHoldLast uses this as an
        // upper bound so it never reads a slot with a tick greater than what
        // was actually written — protects against wrap-around false positives
        // when `tick` is below all stored entries.
        private int _newestTick;
        private bool _anyWritten;

        public static InputBuffer<TInput> Create()
        {
            return new InputBuffer<TInput> { _slots = new Slot[Capacity] };
        }

        public void Store(int tick, in TInput input)
        {
            if (_slots == null) _slots = new Slot[Capacity];

            int index = Mod(tick, Capacity);
            _slots[index].Tick = tick;
            _slots[index].Input = input;
            _slots[index].Valid = true;

            if (!_anyWritten || tick > _newestTick)
            {
                _newestTick = tick;
                _anyWritten = true;
            }
        }

        public bool TryGet(int tick, out TInput input)
        {
            if (_slots == null || !_anyWritten)
            {
                input = default;
                return false;
            }

            int index = Mod(tick, Capacity);
            if (_slots[index].Valid && _slots[index].Tick == tick)
            {
                input = _slots[index].Input;
                return true;
            }

            input = default;
            return false;
        }

        /// <summary>
        /// Returns the most recent stored input whose tick is <c>&lt;= tick</c>.
        /// Skips gaps in the ring so an untagged tick inherits the prior one —
        /// matches the step-6 hold-last behaviour on the server.
        /// </summary>
        public bool GetOrHoldLast(int tick, out TInput input)
        {
            if (_slots == null || !_anyWritten)
            {
                input = default;
                return false;
            }

            // Clamp search upper bound to the newest known tick. Any slot with
            // Tick > _newestTick would be a wrap-around artifact from an older
            // generation of stores, so treat those as absent.
            int upper = tick < _newestTick ? tick : _newestTick;

            // Walk up to Capacity-1 ticks backwards — further than that and the
            // slot has been overwritten by a newer generation, so the data is
            // definitively lost.
            int lower = upper - (Capacity - 1);
            for (int t = upper; t >= lower; t--)
            {
                int index = Mod(t, Capacity);
                if (_slots[index].Valid && _slots[index].Tick == t)
                {
                    input = _slots[index].Input;
                    return true;
                }
            }

            input = default;
            return false;
        }

        // Ticks can go negative during the first few frames of a session
        // (NetworkTickSystem counts from 0 but client LocalTime can briefly
        // trail into the negatives). C# '%' preserves sign for negatives, so
        // normalize to a non-negative index explicitly.
        private static int Mod(int tick, int capacity)
        {
            int r = tick % capacity;
            return r < 0 ? r + capacity : r;
        }
    }
}
