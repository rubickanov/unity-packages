using System;
using System.Collections.Generic;

namespace Rubickanov.EQS
{
    /// <summary>
    /// Produces a list of candidate items for a query.
    /// Subclass and override <see cref="Generate"/> to implement custom generators.
    /// </summary>
    [Serializable]
    public abstract class EQSGenerator
    {
        /// <summary>
        /// Generates candidate items. The list will be cleared before this call.
        /// </summary>
        public abstract void Generate(EQSQueryContext context, List<EQSItem> results);
    }
}
