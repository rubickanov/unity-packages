using System;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Runtime
{
    /// <summary>
    /// Base class for all behavior tree nodes. Subclass and override <see cref="OnTick"/> to implement logic.
    /// </summary>
    [Serializable]
    public abstract class BTNode
    {
        [SerializeField, HideInInspector] private string _guid = "";
        [SerializeField, HideInInspector] private Vector2 _position;

        public string Guid { get => _guid; set => _guid = value; }
        public Vector2 Position { get => _position; set => _position = value; }

        [NonSerialized] private BTStatus _lastStatus;
        public BTStatus LastStatus => _lastStatus;

        /// <summary>
        /// Executes this node and returns its status. Called by the parent node or runner each frame.
        /// </summary>
        public BTStatus Tick(BTContext ctx)
        {
            var status = OnTick(ctx);
            _lastStatus = status;
            return status;
        }

        protected abstract BTStatus OnTick(BTContext ctx);

        /// <summary>
        /// Called when a parent node interrupts this node before it finishes. Resets state.
        /// </summary>
        public virtual void Abort()
        {
            _lastStatus = BTStatus.Failure;
        }

        /// <summary>
        /// Creates a deep copy of this node with a new GUID.
        /// </summary>
        public virtual BTNode Clone()
        {
            var clone = (BTNode)MemberwiseClone();
            clone._guid = System.Guid.NewGuid().ToString();
            return clone;
        }
    }
}