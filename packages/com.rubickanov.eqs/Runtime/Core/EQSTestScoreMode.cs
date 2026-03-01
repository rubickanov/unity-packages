namespace Rubickanov.EQS
{
    public enum EQSTestScoreMode
    {
        /// <summary>Higher raw score = better.</summary>
        Score,

        /// <summary>Inverts the score: finalScore = 1 - rawScore.</summary>
        InverseScore,

        /// <summary>Filter only: items with negative score are discarded, no score contribution.</summary>
        FilterOnly
    }
}
