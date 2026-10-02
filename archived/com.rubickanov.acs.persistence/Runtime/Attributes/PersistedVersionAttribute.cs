using System;

namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// Current schema version of this aspect's persisted fields. Stored in the snapshot
    /// as <c>AspectData.Version</c>. On restore the package runs registered
    /// <see cref="IAspectMigrator"/> migrators to bridge any gap between the snapshot's
    /// version and this value.
    /// <para/>
    /// Missing attribute is treated as version 0 — both on save and on load — so existing
    /// aspects stay forward-compatible without any change.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class PersistedVersionAttribute : Attribute
    {
        public int Version { get; }

        public PersistedVersionAttribute(int version)
        {
            if (version < 0)
                throw new ArgumentOutOfRangeException(nameof(version), "[PersistedVersion] must be >= 0.");
            Version = version;
        }
    }
}
