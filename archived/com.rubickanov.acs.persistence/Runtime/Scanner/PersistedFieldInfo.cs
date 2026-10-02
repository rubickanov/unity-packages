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
        Enum,
    }

    internal readonly struct PersistedFieldInfo
    {
        public readonly FieldInfo Field;
        public readonly PersistedFieldKind Kind;
        public readonly Type ValueType; // T for ReactiveProperty<T>, list/set element, dictionary value.
        public readonly Type KeyType;   // TKey for ObservableDictionary<TKey, TValue>; null for Reactive / List / HashSet kinds.
        public readonly PersistedEnumMode EnumMode; // only meaningful when Kind == Enum.

        public PersistedFieldInfo(FieldInfo field, PersistedFieldKind kind, Type valueType, Type keyType)
            : this(field, kind, valueType, keyType, default)
        {
        }

        public PersistedFieldInfo(FieldInfo field, PersistedFieldKind kind, Type valueType, Type keyType, PersistedEnumMode enumMode)
        {
            Field = field;
            Kind = kind;
            ValueType = valueType;
            KeyType = keyType;
            EnumMode = enumMode;
        }
    }
}
