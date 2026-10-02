using Rubickanov.ACS.Runtime.Netcode;
using UnityEngine;

namespace Experiments
{
    /// <summary>
    /// Per-tick input for the experiment prefab. <c>unmanaged</c> so the
    /// prediction pipeline's unsafe byte-copy serialization can ship it.
    /// </summary>
    public struct ExperimentInputCommand : IInputCommand
    {
        public Vector2 Move;
    }
}
