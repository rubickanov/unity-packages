using System;
using R3;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Persistence
{
    internal sealed class PersistedReactiveBinding<T> : PersistedFieldBinding
    {
        private readonly ReactiveProperty<T> _reactive;

        public PersistedReactiveBinding(ReactiveProperty<T> reactive)
        {
            Debug.Assert(reactive != null, "PersistedReactiveBinding: reactive is null — factory must reject uninitialized [PersistedState] fields.");
            _reactive = reactive;
        }

        public override object ReadValue()
        {
            return _reactive.Value;
        }

        public override void WriteValue(object value)
        {
            // Unboxing a null into a non-nullable value type throws NullReferenceException,
            // which the restore loop's InvalidCastException-only catch would let through
            // and poison the whole restore. Surface it as the cast mismatch it really is.
            if (value == null && default(T) != null)
                throw new InvalidCastException(
                    $"Cannot write null into ReactiveProperty<{typeof(T).Name}> — target is a non-nullable value type.");

            _reactive.Value = (T)value;
        }
    }
}
