using System;

namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Marks a field inside an <see cref="IEntityAspect"/> as part of the persisted
    /// state. <see cref="PersistenceScanner"/> picks up these fields; everything
    /// else on the aspect is treated as runtime-only and never enters a snapshot.
    /// <para/>
    /// Supported field types:
    /// <list type="bullet">
    ///   <item><c>ReactiveProperty&lt;T&gt;</c> where T is a value type or string.</item>
    ///   <item><c>ObservableList&lt;T&gt;</c> / <c>ObservableHashSet&lt;T&gt;</c>
    ///         with T a value type or string.</item>
    ///   <item><c>ObservableDictionary&lt;K,V&gt;</c> with both K and V value type or string.</item>
    /// </list>
    /// A field marked with both <c>[PersistedState]</c> and <c>[Replicated]</c> is
    /// fine — the two pipelines are independent.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class PersistedStateAttribute : Attribute
    {
    }
}
