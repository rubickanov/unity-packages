using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// World-space positional grips supplied by gameplay; rotations remain those of the animated pose.
    /// Either target may be absent. Weight is clamped to 0..1; release fades each hand independently.
    /// Required mount holds, ragdoll and get-up take priority. No knowledge of items or networking here.
    /// </summary>
    public interface IWorkHands
    {
        Transform Left { get; }
        Transform Right { get; }
        float Weight { get; }
    }
}
