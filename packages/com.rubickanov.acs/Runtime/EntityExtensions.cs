using System;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Convenience helpers for attaching pure-C# behaviour to an entity.
    /// </summary>
    public static class EntityExtensions
    {
        /// <summary>
        /// Attaches a pure <see cref="IEntityLogic"/> to <paramref name="entity"/>
        /// and auto-disposes it when the entity is destroyed. AttachLogic
        /// guarantees exactly one <c>Dispose()</c> call through its own
        /// <see cref="IEntity.Destroyed"/> hook — the hook goes through that
        /// path at most once (subscription is removed when it fires, and
        /// <see cref="IEntity.Destroyed"/> itself fires at most once per
        /// entity).
        /// <para/>
        /// AttachLogic cannot observe manual <c>logic.Dispose()</c> calls
        /// because they bypass the framework entirely. If the caller disposes
        /// the logic by hand and the entity is later destroyed, the Destroyed
        /// hook will still fire and call <c>Dispose()</c> a second time.
        /// Implementations of <see cref="IEntityLogic"/> are therefore required
        /// to be idempotent on <c>Dispose</c> (the standard
        /// <c>if (_disposed) return; _disposed = true; ...</c> pattern) — see
        /// <see cref="IEntityLogic"/>.
        /// <para/>
        /// Typical use:
        /// <code>
        /// var entity = new Entity();
        /// entity.AttachLogic(new HealthRegenLogic(entity));
        /// entity.Dispose(); // HealthRegenLogic.Dispose() fires here
        /// </code>
        /// </summary>
        public static TLogic AttachLogic<TLogic>(this IEntity entity, TLogic logic)
            where TLogic : IEntityLogic
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (logic == null) throw new ArgumentNullException(nameof(logic));

            Action<IEntity>? handler = null;
            handler = _ =>
            {
                // Unsubscribe before dispose so if the logic re-enters Destroyed
                // for any reason it cannot come back through this handler.
                entity.Destroyed -= handler!;
                logic.Dispose();
            };
            entity.Destroyed += handler;
            return logic;
        }
    }
}
