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

        public EQSScoredItem? BestItem => Items.Count > 0 ? Items[0] : null;

        public EQSQueryResult(bool success, IReadOnlyList<EQSScoredItem> items)
        {
            Success = success;
            Items = items;
        }
    }
}
