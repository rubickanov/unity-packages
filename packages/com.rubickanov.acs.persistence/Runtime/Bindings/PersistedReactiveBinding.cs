using R3;

namespace Rubickanov.ACS.Runtime.Persistence
{
    internal sealed class PersistedReactiveBinding<T> : PersistedFieldBinding
    {
        private readonly ReactiveProperty<T> _reactive;

        public PersistedReactiveBinding(ReactiveProperty<T> reactive)
        {
            _reactive = reactive;
        }

        public override object ReadValue()
        {
            return _reactive.Value;
        }

        public override void WriteValue(object value)
        {
            _reactive.Value = (T)value;
        }
    }
}
