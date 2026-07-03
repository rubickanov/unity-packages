using UnityEngine;

namespace Rubickanov.EQS
{
    /// <summary>
    /// A single candidate produced by a generator.
    /// Position is always available. Object is set only for actor-based generators.
    /// </summary>
    public readonly struct EQSItem
    {
        public readonly Vector3 Position;
        public readonly GameObject? Object;

        public EQSItem(Vector3 position, GameObject? obj = null)
        {
            Position = position;
            Object = obj;
        }
    }
}
