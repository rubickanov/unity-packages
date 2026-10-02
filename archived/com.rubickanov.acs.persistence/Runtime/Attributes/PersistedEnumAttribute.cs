using System;

namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Explicit opt-in for enum fields marked <c>[PersistedState]</c>. The scanner
    /// refuses bare <c>ReactiveProperty&lt;TEnum&gt;</c> without this attribute because
    /// the default encoding choice matters for save-file stability — picking wrong
    /// silently corrupts old saves when a member is reordered or renamed.
    /// <para/>
    /// <list type="bullet">
    ///   <item><see cref="PersistedEnumMode.ByName"/> — default; snapshot stores the
    ///         member name. Safe against reorders, breaks only on renames (where a
    ///         migrator is obvious).</item>
    ///   <item><see cref="PersistedEnumMode.ByValue"/> — snapshot stores the underlying
    ///         integer. Compact, but any reorder or insert silently breaks old saves.</item>
    /// </list>
    /// Applies only to <c>ReactiveProperty&lt;TEnum&gt;</c> fields; enum elements inside
    /// <c>ObservableList</c> / <c>ObservableHashSet</c> / <c>ObservableDictionary</c>
    /// are not yet supported.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class PersistedEnumAttribute : Attribute
    {
        public PersistedEnumMode Mode { get; }

        public PersistedEnumAttribute(PersistedEnumMode mode = PersistedEnumMode.ByName)
        {
            Mode = mode;
        }
    }
}
