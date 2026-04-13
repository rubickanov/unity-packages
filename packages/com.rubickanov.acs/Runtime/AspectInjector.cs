using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Injects aspect instances into fields marked with <see cref="AspectAttribute"/>.
    /// Caches reflection data per component type for performance.
    /// </summary>
    public static class AspectInjector
    {
        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new();
        private static readonly Dictionary<Type, Func<IEntity, object>> RequireDelegateCache = new();

        // Target IEntity rather than MonoEntity so the injector works for both the
        // Unity-bound context and pure POCO Entity — same reflection path, no
        // UnityEngine dependency leaking into the lookup.
        private static readonly MethodInfo RequireMethod =
            typeof(IEntity).GetMethod(nameof(IEntity.Require))!;

        public static void Inject(IEntity context, object component)
        {
            var componentType = component.GetType();

            if (!FieldCache.TryGetValue(componentType, out var fields))
            {
                fields = CollectAspectFields(componentType);
                FieldCache[componentType] = fields;
            }

            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                var aspect = GetOrBuildRequireDelegate(field.FieldType)(context);
                // FieldInfo.SetValue is kept instead of Expression.Assign(Field, …) because
                // every [Aspect] field in real usage is declared `readonly` (initonly in IL),
                // which Expression.Assign rejects at Compile() time. DynamicMethod + stfld
                // would work but breaks under IL2CPP. The main hot-path win comes from
                // eliminating MethodInfo.Invoke above — that dominates the per-field cost.
                field.SetValue(component, aspect);
            }
        }

        private static Func<IEntity, object> GetOrBuildRequireDelegate(Type aspectType)
        {
            if (RequireDelegateCache.TryGetValue(aspectType, out var del))
                return del;

            var closed = RequireMethod.MakeGenericMethod(aspectType);
            var ctxParam = Expression.Parameter(typeof(IEntity), "ctx");
            // Require<T>() returns T (reference type); Convert to object is a no-op reference cast.
            var body = Expression.Convert(Expression.Call(ctxParam, closed), typeof(object));
            del = Expression.Lambda<Func<IEntity, object>>(body, ctxParam).Compile();
            RequireDelegateCache[aspectType] = del;
            return del;
        }

        private static FieldInfo[] CollectAspectFields(Type type)
        {
            var result = new List<FieldInfo>();
            var current = type;

            while (current != null && current != typeof(object))
            {
                var fields = current.GetFields(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

                for (int i = 0; i < fields.Length; i++)
                {
                    if (fields[i].IsDefined(typeof(AspectAttribute), false))
                        result.Add(fields[i]);
                }

                current = current.BaseType;
            }

            return result.ToArray();
        }
    }
}
