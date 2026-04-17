using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Persistence
{
    internal static class PersistedFieldBindingFactory
    {
        // Compiled constructor delegates keyed by the closed binding Type. One cache entry per
        // (Kind, ValueType [, KeyType]) triple. Expression.Compile ~5–10× faster than
        // Activator.CreateInstance and allocates no params-array on the hot path.
        private static readonly ConcurrentDictionary<Type, Func<object, PersistedFieldBinding>> PlainCtors = new();
        private static readonly ConcurrentDictionary<Type, Func<object, PersistedEnumMode, PersistedFieldBinding>> EnumCtors = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PlainCtors.Clear();
            EnumCtors.Clear();
        }

        public static PersistedFieldBinding Create(object aspect, PersistedFieldInfo info)
        {
            var raw = info.Field.GetValue(aspect);
            if (raw == null)
                throw new InvalidOperationException(
                    $"[acs.persistence] Aspect '{aspect.GetType().FullName}' field '{info.Field.Name}' " +
                    $"marked [PersistedState] is null. [PersistedState] fields must be initialized at declaration " +
                    $"(e.g. 'public readonly ReactiveProperty<int> Health = new(100);').");

            switch (info.Kind)
            {
                case PersistedFieldKind.Reactive:
                {
                    var bindingType = typeof(PersistedReactiveBinding<>).MakeGenericType(info.ValueType);
                    return GetPlainCtor(bindingType).Invoke(raw);
                }
                case PersistedFieldKind.Enum:
                {
                    var bindingType = typeof(PersistedEnumBinding<>).MakeGenericType(info.ValueType);
                    return GetEnumCtor(bindingType).Invoke(raw, info.EnumMode);
                }
                case PersistedFieldKind.ObservableList:
                {
                    var bindingType = typeof(PersistedListBinding<>).MakeGenericType(info.ValueType);
                    return GetPlainCtor(bindingType).Invoke(raw);
                }
                case PersistedFieldKind.ObservableHashSet:
                {
                    var bindingType = typeof(PersistedHashSetBinding<>).MakeGenericType(info.ValueType);
                    return GetPlainCtor(bindingType).Invoke(raw);
                }
                case PersistedFieldKind.ObservableDictionary:
                {
                    var bindingType = typeof(PersistedDictionaryBinding<,>).MakeGenericType(info.KeyType, info.ValueType);
                    return GetPlainCtor(bindingType).Invoke(raw);
                }
                default:
                    throw new InvalidOperationException($"Unknown PersistedFieldKind: {info.Kind}");
            }
        }

        private static Func<object, PersistedFieldBinding> GetPlainCtor(Type bindingType)
        {
            return PlainCtors.GetOrAdd(bindingType, static t =>
            {
                var ctor = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
                var rawParam = Expression.Parameter(typeof(object), "raw");
                var typedRaw = Expression.Convert(rawParam, ctor.GetParameters()[0].ParameterType);
                var body = Expression.New(ctor, typedRaw);
                return Expression.Lambda<Func<object, PersistedFieldBinding>>(body, rawParam).Compile();
            });
        }

        private static Func<object, PersistedEnumMode, PersistedFieldBinding> GetEnumCtor(Type bindingType)
        {
            return EnumCtors.GetOrAdd(bindingType, static t =>
            {
                var ctor = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
                var parameters = ctor.GetParameters();
                var rawParam = Expression.Parameter(typeof(object), "raw");
                var modeParam = Expression.Parameter(typeof(PersistedEnumMode), "mode");
                var typedRaw = Expression.Convert(rawParam, parameters[0].ParameterType);
                var body = Expression.New(ctor, typedRaw, modeParam);
                return Expression.Lambda<Func<object, PersistedEnumMode, PersistedFieldBinding>>(body, rawParam, modeParam).Compile();
            });
        }
    }
}
