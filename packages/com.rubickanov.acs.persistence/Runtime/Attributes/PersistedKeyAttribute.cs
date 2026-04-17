using System;

namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Stable snapshot key for an aspect, decoupled from its CLR <c>Type.FullName</c>.
    /// Snapshots write this key; restores look up aspects by it first. Without the
    /// attribute the package falls back to <c>Type.FullName</c>, matching the pre-1.2
    /// behaviour exactly.
    /// <para/>
    /// Pair with <see cref="PersistedAliasAttribute"/> to accept old keys written
    /// before the rename.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class PersistedKeyAttribute : Attribute
    {
        public string Key { get; }

        public PersistedKeyAttribute(string key)
        {
            // Trim first so "  hero  " doesn't silently mismatch "hero" at lookup time.
            key = key?.Trim();
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("[PersistedKey] key must be a non-empty, non-whitespace string.", nameof(key));
            Key = key;
        }
    }
}
