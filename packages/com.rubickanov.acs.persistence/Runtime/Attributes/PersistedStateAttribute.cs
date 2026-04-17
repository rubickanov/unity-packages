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
    /// <para/>
    /// <c>[PersistedState]</c> does not inherit through CLR attribute reflection
    /// (<see cref="AttributeUsageAttribute.Inherited"/> is <c>false</c>), but the
    /// scanner walks the aspect's type hierarchy explicitly, so fields declared on
    /// a base aspect class are always included in its derived aspects' snapshots.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class PersistedStateAttribute : Attribute
    {
    }
}
