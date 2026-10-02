using System;

namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Registers an old snapshot key that should resolve to this aspect at restore time.
    /// Applied repeatedly for multi-step renames. Aliases are resolve-only — snapshots
    /// always write the current canonical key (from <see cref="PersistedKeyAttribute"/>
    /// or <c>Type.FullName</c>).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class PersistedAliasAttribute : Attribute
    {
        public string OldKey { get; }

        public PersistedAliasAttribute(string oldKey)
        {
            oldKey = oldKey?.Trim();
            if (string.IsNullOrEmpty(oldKey))
                throw new ArgumentException("[PersistedAlias] oldKey must be a non-empty, non-whitespace string.", nameof(oldKey));
            OldKey = oldKey;
        }
    }
}
