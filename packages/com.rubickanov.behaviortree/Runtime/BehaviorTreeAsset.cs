using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Runtime
{
    /// <summary>
    /// ScriptableObject that stores a serialized behavior tree. Call <see cref="CreateInstance"/> to get a runtime copy.
    /// </summary>
    [CreateAssetMenu(fileName = "BehaviorTree", menuName = "AI/Behavior Tree")]
    public class BehaviorTreeAsset : ScriptableObject
    {
        [SerializeReference] private BTNode? _root;
        [SerializeReference] private List<BTNode> _orphans = new();

        public BTNode? Root => _root;
        public IReadOnlyList<BTNode> Orphans => _orphans;

        public BTNode? CreateInstance() => _root?.Clone();
    }
}