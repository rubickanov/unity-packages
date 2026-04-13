using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Pure-C# aspect container. Use when an entity should NOT be tied to a
    /// Unity <c>GameObject</c>: pocket entities (an item inside an inventory, a
    /// buff with no visual), headless simulations (server authority running in
    /// a console host), or fast edit-mode tests that exercise aspect logic
    /// without booting the Unity player loop.
    /// <para/>
    /// Pass a <see cref="WorldCore"/> to <see cref="Entity(WorldCore)"/> to opt
    /// into automatic Register/Unregister — the same way <see cref="MonoEntity"/>
    /// auto-integrates with <c>World.Instance</c>. The parameterless ctor keeps
    /// the entity standalone for callers that want to drive registration themselves.
    /// <para/>
    /// The lifetime is managed explicitly via <see cref="Dispose"/>. Dispose is
    /// idempotent: the <see cref="Destroyed"/> event fires at most once, and the
    /// aspect dictionary is cleared so subscribers observe an empty entity.
    /// </summary>
    public sealed class Entity : IEntity, IDisposable
    {
        /// <inheritdoc/>
        public event Action<IEntity>? Destroyed;

        private readonly Dictionary<Type, object> _aspects = new();
        private readonly WorldCore? _core;
        private bool _disposed;

        /// <summary>
        /// Creates a standalone pure-C# entity with no registry integration.
        /// Callers that want the entity to participate in <see cref="WorldCore"/>
        /// queries must call <c>core.Register(entity, typeof(T))</c> manually —
        /// or prefer the <see cref="Entity(WorldCore)"/> overload.
        /// </summary>
        public Entity()
        {
        }

        /// <summary>
        /// Creates a pure-C# entity that auto-registers with <paramref name="core"/>
        /// on every first <see cref="Require{T}"/> and auto-unregisters from it on
        /// <see cref="Dispose"/>. Mirrors the implicit <see cref="MonoEntity"/> ↔
        /// <c>World.Instance</c> integration so pure-core consumers do not have to
        /// mirror every <c>Require</c> with a manual <c>Register</c>.
        /// </summary>
        public Entity(WorldCore core)
        {
            _core = core;
        }

        /// <inheritdoc/>
        public T Require<T>() where T : class, IEntityAspect, new()
        {
            var type = typeof(T);
            if (_aspects.TryGetValue(type, out var existing))
                return (T)existing;
            var instance = new T();
            _aspects[type] = instance;
            _core?.Register(this, type);
            return instance;
        }

        /// <inheritdoc/>
        public bool TryGet<T>([NotNullWhen(returnValue: true)] out T? aspect) where T : class, IEntityAspect
        {
            if (_aspects.TryGetValue(typeof(T), out var existing))
            {
                aspect = (T)existing;
                return true;
            }
            aspect = null;
            return false;
        }

        /// <inheritdoc/>
        public bool Has<T>() where T : class, IEntityAspect
        {
            return _aspects.ContainsKey(typeof(T));
        }

        /// <inheritdoc/>
        public IEnumerable<object> GetAllAspects() => _aspects.Values;

        /// <inheritdoc/>
        public Dictionary<Type, object>.KeyCollection AspectTypes => _aspects.Keys;

        /// <summary>
        /// Releases the entity: fires <see cref="Destroyed"/> (once), clears the
        /// aspect dictionary, and drops all <see cref="Destroyed"/> subscribers.
        /// Safe to call twice — second call is a no-op.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Fire Destroyed before unregistering so subscribers can still query
            // the registry while unwinding — matches MonoEntity.OnDestroy ordering.
            Destroyed?.Invoke(this);
            _core?.Unregister(this, _aspects.Keys);
            Destroyed = null;
            _aspects.Clear();
        }
    }
}
