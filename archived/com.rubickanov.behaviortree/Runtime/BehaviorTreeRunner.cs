using System;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Runtime
{
    /// <summary>
    /// MonoBehaviour that drives a behavior tree. Assign a <see cref="BehaviorTreeAsset"/> or call
    /// <see cref="Initialize"/> with a root node, then call <see cref="Tick"/> each frame.
    /// </summary>
    public class BehaviorTreeRunner : MonoBehaviour
    {
        [SerializeField] private BehaviorTreeAsset? _treeAsset;

        public BehaviorTreeAsset? Asset => _treeAsset;
        public BTNode? RuntimeRoot => _root;
        public Blackboard Blackboard { get; } = new();

        private BTNode? _root;
        private float _time;

        /// <summary>
        /// Sets the runtime root node directly (bypassing the asset).
        /// </summary>
        public void Initialize(BTNode root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
        }

        public void EnsureInitialized()
        {
            if (_root == null && _treeAsset != null)
                _root = _treeAsset.CreateInstance();
        }

        /// <summary>
        /// Ticks the tree. Call once per frame from your update loop.
        /// </summary>
        public void Tick(object? owner, float deltaTime, uint tick = 0)
        {
            if (_root == null)
            {
                EnsureInitialized();

                if (_root == null)
                {
                    Debug.LogError($"{nameof(BehaviorTreeRunner)}: root is null. " +
                                   $"Call {nameof(Initialize)}() or assign a {nameof(BehaviorTreeAsset)}.", this);
                    return;
                }
            }

            _time += deltaTime;

            var ctx = new BTContext(owner, Blackboard, deltaTime, _time, tick);
            _root.Tick(ctx);
        }
    }
}