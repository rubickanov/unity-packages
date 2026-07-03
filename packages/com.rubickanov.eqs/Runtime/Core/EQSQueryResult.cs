using System.Collections.Generic;

namespace Rubickanov.EQS
{
    /// <summary>
    /// Result of a completed query. Items are sorted by score descending (best first).
    /// <para>
    /// <b>Lifetime:</b> <see cref="Items"/> points at a buffer owned by the producing
    /// <see cref="EQSQuery"/> and is only valid until the next <c>Start()</c>, <c>RunSync()</c>,
    /// or <c>Reset()</c> on that query. Do not cache the result or its items across queries —
    /// copy what you need (e.g. the best <see cref="EQSScoredItem"/>) instead.
    /// </para>
    /// </summary>
    public readonly struct EQSQueryResult
    {
        public readonly bool Success;

        /// <summary>
        /// Items sorted by score descending. Read-only snapshot — never cast back to a mutable
        /// collection, and never hold it past the next query run (see type remarks).
        /// </summary>
        public readonly IReadOnlyList<EQSScoredItem> Items;

        public EQSQueryResult(bool success, IReadOnlyList<EQSScoredItem> items)
        {
            Success = success;
            Items = items;
        }

        public bool TryGetBest(out EQSScoredItem item)
        {
            if (Items != null && Items.Count > 0)
            {
                item = Items[0];
                return true;
            }

            item = default;
            return false;
        }

        /// <summary>
        /// Copies the top <paramref name="n"/> items (those scoring at least <paramref name="minScore"/>)
        /// into <paramref name="destination"/>, clearing it first. Allocation-free — the caller owns
        /// the buffer. Returns the number of items written.
        /// </summary>
        public int TopN(int n, List<EQSScoredItem> destination, float minScore = 0f)
        {
            destination.Clear();
            if (Items == null || Items.Count == 0 || n <= 0) return 0;

            for (int i = 0; i < Items.Count && destination.Count < n; i++)
            {
                // Items are sorted descending, so the first item below the threshold ends the run.
                if (Items[i].Score < minScore) break;
                destination.Add(Items[i]);
            }

            return destination.Count;
        }
    }
}
