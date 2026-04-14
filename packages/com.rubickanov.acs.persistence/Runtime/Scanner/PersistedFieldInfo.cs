using System;
using System.Reflection;

namespace Rubickanov.ACS.Runtime.Persistence
{
    internal enum PersistedFieldKind
    {
        Reactive,
        ObservableList,
        ObservableDictionary,
        ObservableHashSet,
    }

    internal readonly struct PersistedFieldInfo
    {
        public readonly FieldInfo Field;
        public readonly PersistedFieldKind Kind;
        public readonly Type ValueType; // T for ReactiveProperty<T>, list/set element, dictionary value.
        public readonly Type KeyType;   // dictionary key; null otherwise.

        public PersistedFieldInfo(FieldInfo field, PersistedFieldKind kind, Type valueType, Type keyType)
        {
            Field = field;
            Kind = kind;
            ValueType = valueType;
            KeyType = keyType;
        }
    }
}
