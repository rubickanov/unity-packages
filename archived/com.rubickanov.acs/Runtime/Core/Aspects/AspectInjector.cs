using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Injects aspect instances into fields marked with <see cref="AspectAttribute"/>.
    /// Caches reflection data per component type for performance.
    /// <para/>
    /// The cache is a <see cref="ConcurrentDictionary{TKey,TValue}"/> so future headless
    /// simulations can run injection across threads without racing on cache population.
    /// Under Unity's single-threaded player loop the overhead is negligible.
    /// <para/>
    /// The <c>Require&lt;T&gt;</c> call itself lives in <see cref="AspectResolver"/>, shared
    /// with the persistence package so there is one runtime-typed aspect lookup in the
    /// framework rather than one per consumer.
    /// </summary>
    public static class AspectInjector
    {
        private static readonly ConcurrentDictionary<Type, FieldInfo[]> FieldCache = new();

        // Cached so GetOrAdd doesn't allocate a fresh delegate from the method group on every
        // call — C# 10 has no method-group conversion caching.
        private static readonly Func<Type, FieldInfo[]> CollectAspectFieldsDelegate = CollectAspectFields;

        public static void Inject(IEntity context, object component)
        {
            var componentType = component.GetType();
            var fields = FieldCache.GetOrAdd(componentType, CollectAspectFieldsDelegate);

            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                var aspect = AspectResolver.Require(context, field.FieldType);
                // FieldInfo.SetValue is kept instead of Expression.Assign(Field, …) because
                // every [Aspect] field in real usage is declared `readonly` (initonly in IL),
                // which Expression.Assign rejects at Compile() time. DynamicMethod + stfld
                // would work but breaks under IL2CPP. With the Require call now going through
                // AspectResolver's cached dispatcher, this write is what dominates the
                // per-field cost — and there is no AOT-safe way to beat it.
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

            // CopyTo onto a pre-sized array avoids the temporary that List.ToArray allocates
            // internally. Cold path (cached per type), but free savings.
            var array = new FieldInfo[result.Count];
            result.CopyTo(array);
            return array;
        }
    }
}
