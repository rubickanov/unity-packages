using System;
using System.Reflection;
using UnityEngine;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Non-generic dispatcher for <see cref="SingletonMonoEntity{T}"/> play-start resets.
    /// Unity refuses <c>[RuntimeInitializeOnLoadMethod]</c> on methods declared inside an
    /// open generic class, so we hang the hook here and walk every concrete subclass of
    /// <see cref="SingletonMonoEntity{T}"/> to invoke its private
    /// <c>ResetInstanceOnPlayStart</c> reflectively on the closed generic base.
    /// <para/>
    /// One-shot cost at <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>
    /// — the reflection walk runs once per play session.
    /// </summary>
    internal static class SingletonMonoEntityResetter
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAllOnPlayStart()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type?[] types;
                try
                {
                    types = assemblies[i].GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // Partially-loadable assemblies (e.g. Editor-only refs missing in Player
                    // builds) still expose the types they managed to resolve.
                    types = ex.Types;
                }

                for (int t = 0; t < types.Length; t++)
                {
                    var type = types[t];
                    if (type == null || type.IsAbstract || type.IsGenericTypeDefinition) continue;

                    var closedBase = FindClosedSingletonBase(type);
                    if (closedBase == null) continue;

                    // The ResetInstanceOnPlayStart method lives on the closed generic base —
                    // each distinct T has its own static Instance slot to null.
                    var reset = closedBase.GetMethod(
                        "ResetInstanceOnPlayStart",
                        BindingFlags.NonPublic | BindingFlags.Static);
                    reset?.Invoke(null, null);
                }
            }
        }

        private static Type? FindClosedSingletonBase(Type type)
        {
            var baseType = type.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                if (baseType.IsGenericType &&
                    baseType.GetGenericTypeDefinition() == typeof(SingletonMonoEntity<>))
                {
                    return baseType;
                }
                baseType = baseType.BaseType;
            }
            return null;
        }
    }
}
