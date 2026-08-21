using System;
using System.Collections.Concurrent;
using System.Reflection;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Persistence
{
    internal static class PersistedFieldBindingFactory
    {
        // ConstructorInfo keyed by the closed binding Type. One cache entry per
        // (Kind, ValueType [, KeyType]) triple.
        //
        // This used to cache Expression.Compile'd factory delegates, which are faster and
        // allocate no params-array. They are also not IL2CPP-safe — no runtime IL emitter
        // there, so Compile() degrades to an interpreter. ConstructorInfo.Invoke is the same
        // trade ACS.Netcode makes in ReplicatedFieldBindingFactory, and it costs one object[]
        // per binding construction. That is bind-time, not per-tick: Create runs once per
        // [PersistedState] field per snapshot/restore, never inside the frame loop.
        private static readonly ConcurrentDictionary<Type, ConstructorInfo> PlainCtors = new();
        private static readonly ConcurrentDictionary<Type, ConstructorInfo> EnumCtors = new();

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
                    return InvokePlainCtor(bindingType, raw);
                }
                case PersistedFieldKind.Enum:
                {
                    var bindingType = typeof(PersistedEnumBinding<>).MakeGenericType(info.ValueType);
                    return InvokeEnumCtor(bindingType, raw, info.EnumMode);
                }
                case PersistedFieldKind.ObservableList:
                {
                    var bindingType = typeof(PersistedListBinding<>).MakeGenericType(info.ValueType);
                    return InvokePlainCtor(bindingType, raw);
                }
                case PersistedFieldKind.ObservableHashSet:
                {
                    var bindingType = typeof(PersistedHashSetBinding<>).MakeGenericType(info.ValueType);
                    return InvokePlainCtor(bindingType, raw);
                }
                case PersistedFieldKind.ObservableDictionary:
                {
                    var bindingType = typeof(PersistedDictionaryBinding<,>).MakeGenericType(info.KeyType, info.ValueType);
                    return InvokePlainCtor(bindingType, raw);
                }
                default:
                    throw new InvalidOperationException($"Unknown PersistedFieldKind: {info.Kind}");
            }
        }

        private static PersistedFieldBinding InvokePlainCtor(Type bindingType, object raw)
        {
            var ctor = PlainCtors.GetOrAdd(bindingType, static t => FirstCtor(t));
            return InvokeCtorSafe(ctor, new[] { raw }, bindingType);
        }

        private static PersistedFieldBinding InvokeEnumCtor(Type bindingType, object raw, PersistedEnumMode mode)
        {
            var ctor = EnumCtors.GetOrAdd(bindingType, static t => FirstCtor(t));
            return InvokeCtorSafe(ctor, new object[] { raw, mode }, bindingType);
        }

        private static ConstructorInfo FirstCtor(Type bindingType)
        {
            return bindingType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
        }

        // Wraps ConstructorInfo.Invoke and translates the opaque TargetInvocationException that
        // IL2CPP throws for stripped generic specializations into a targeted error pointing the
        // user at link.xml. Rethrows unrecognised exceptions so real bugs inside the ctor still
        // surface. Mirrors ReplicatedFieldBindingFactory.InvokeCtorSafe in ACS.Netcode.
        private static PersistedFieldBinding InvokeCtorSafe(ConstructorInfo ctor, object[] args, Type bindingType)
        {
            try
            {
                return (PersistedFieldBinding)ctor.Invoke(args);
            }
            catch (TargetInvocationException ex)
                when (ex.InnerException is NotSupportedException
                          || ex.InnerException is MissingMethodException
                          || ex.InnerException is TypeLoadException)
            {
                Debug.LogError(
                    $"[acs.persistence] Failed to construct {bindingType.FullName}. " +
                    $"Most likely IL2CPP stripped the closed generic — add the element / value type " +
                    $"to Assets/link.xml with preserve=\"all\". Inner: {ex.InnerException}");
                throw;
            }
        }
    }
}
