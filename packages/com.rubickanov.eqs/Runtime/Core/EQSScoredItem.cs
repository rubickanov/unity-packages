using UnityEngine;

namespace Rubickanov.EQS
{
    /// <summary>
    /// An item with its final computed score after all tests.
    /// </summary>
    public readonly struct EQSScoredItem
    {
        public readonly Vector3 Position;
        public readonly GameObject? Object;
        public readonly float Score;

        public EQSScoredItem(Vector3 position, GameObject? obj, float score)
        {
            Position = position;
            Object = obj;
            Score = score;
        }
    }
}
