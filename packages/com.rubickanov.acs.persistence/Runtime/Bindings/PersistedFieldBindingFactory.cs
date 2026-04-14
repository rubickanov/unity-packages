using System;

namespace Rubickanov.ACS.Runtime.Persistence
{
    internal static class PersistedFieldBindingFactory
    {
        public static PersistedFieldBinding Create(object aspect, PersistedFieldInfo info)
        {
            var raw = info.Field.GetValue(aspect);

            switch (info.Kind)
            {
                case PersistedFieldKind.Reactive:
                {
                    var bindingType = typeof(PersistedReactiveBinding<>).MakeGenericType(info.ValueType);
                    return (PersistedFieldBinding)Activator.CreateInstance(bindingType, raw);
                }
                case PersistedFieldKind.ObservableList:
                {
                    var bindingType = typeof(PersistedListBinding<>).MakeGenericType(info.ValueType);
                    return (PersistedFieldBinding)Activator.CreateInstance(bindingType, raw);
                }
                case PersistedFieldKind.ObservableHashSet:
                {
                    var bindingType = typeof(PersistedHashSetBinding<>).MakeGenericType(info.ValueType);
                    return (PersistedFieldBinding)Activator.CreateInstance(bindingType, raw);
                }
                case PersistedFieldKind.ObservableDictionary:
                {
                    var bindingType = typeof(PersistedDictionaryBinding<,>).MakeGenericType(info.KeyType, info.ValueType);
                    return (PersistedFieldBinding)Activator.CreateInstance(bindingType, raw);
                }
                default:
                    throw new InvalidOperationException($"Unknown PersistedFieldKind: {info.Kind}");
            }
        }
    }
}
