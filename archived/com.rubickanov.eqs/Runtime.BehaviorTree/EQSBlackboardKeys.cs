using Rubickanov.BehaviorTree.Runtime;
using UnityEngine;

namespace Rubickanov.EQS
{
    public static class EQSBlackboardKeys
    {
        public static readonly BlackboardKey<Vector3> BestPosition = new("EQS.BestPosition");
        public static readonly BlackboardKey<Vector3> ReferencePosition = new("EQS.ReferencePosition");
    }
}
