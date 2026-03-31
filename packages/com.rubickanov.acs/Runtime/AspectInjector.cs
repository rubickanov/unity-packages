using System;
using System.Collections.Generic;
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
        private static readonly Dictionary<Type, MethodInfo> RequireCache = new();

        private static readonly MethodInfo RequireMethod =
            typeof(EntityContext).GetMethod(nameof(EntityContext.Require))!;

        public static void Inject(EntityContext context, object component)
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
                var aspectType = field.FieldType;

                if (!RequireCache.TryGetValue(aspectType, out var requireGeneric))
                {
                    requireGeneric = RequireMethod.MakeGenericMethod(aspectType);
                    RequireCache[aspectType] = requireGeneric;
                }

                var aspect = requireGeneric.Invoke(context, null);
                field.SetValue(component, aspect);
            }
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
