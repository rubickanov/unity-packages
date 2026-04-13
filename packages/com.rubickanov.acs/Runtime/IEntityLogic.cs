using System;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Marker contract for reactive, non-<c>MonoBehaviour</c> behaviour attached
    /// to an <see cref="IEntity"/>. Holds subscriptions and disposes them when
    /// the owning entity is destroyed (auto-wired by
    /// <see cref="EntityExtensions.AttachLogic"/>).
    /// <para/>
    /// This is the default tier for ~80% of aspect-reactive behaviour: plain C#
    /// with no Unity lifecycle, no GameObject, no Update. Implementations should
    /// wire every subscription in the constructor and release them in
    /// <see cref="IDisposable.Dispose"/>. Intentionally empty beyond
    /// <see cref="IDisposable"/> — the marker exists to tag "this is ACS logic"
    /// for readability and for future inspector/diagnostics tooling.
    /// <para/>
    /// <b>Dispose must be idempotent.</b> If the caller disposes the logic
    /// manually and the owning entity is later destroyed,
    /// <see cref="EntityExtensions.AttachLogic"/> will still invoke
    /// <c>Dispose</c> a second time — it has no way to observe the manual call.
    /// Use the standard pattern:
    /// <code>
    /// public void Dispose()
    /// {
    ///     if (_disposed) return;
    ///     _disposed = true;
    ///     _sub.Dispose();
    /// }
    /// </code>
    /// </summary>
    public interface IEntityLogic : IDisposable
    {
    }
}
