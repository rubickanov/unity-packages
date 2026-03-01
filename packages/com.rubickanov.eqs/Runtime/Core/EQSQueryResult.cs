using System.Collections.Generic;

namespace Rubickanov.EQS
{
    /// <summary>
    /// Result of a completed query. Items are sorted by score descending (best first).
    /// </summary>
    public readonly struct EQSQueryResult
    {
        public readonly bool Success;
        public readonly IReadOnlyList<EQSScoredItem> Items;

        public EQSQueryResult(bool success, IReadOnlyList<EQSScoredItem> items)
        {
            Success = success;
            Items = items;
        }

        public bool TryGetBest(out EQSScoredItem item)
        {
            if (Items.Count > 0)
            {
                item = Items[0];
                return true;
            }

            item = default;
            return false;
        }
    }
}
