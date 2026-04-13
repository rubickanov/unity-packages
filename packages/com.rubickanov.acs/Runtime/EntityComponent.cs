using System;
using R3;
using UnityEngine;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Base class for entity components. Override <see cref="OnSubscribe"/> to subscribe to aspect events;
    /// subscriptions are automatically disposed on <see cref="OnDisable"/>.
    /// Mark aspect fields with <see cref="AspectAttribute"/> for automatic injection in Awake.
    /// </summary>
    public abstract class EntityComponent : MonoBehaviour, IEntityComponent
    {
        private MonoEntity? _context;
        private DisposableBag _disposables;

        protected MonoEntity Context
        {
            get
            {
                if (_context != null) return _context;
                _context = GetComponentInParent<MonoEntity>();
                if (_context == null)
                    throw new InvalidOperationException(
                        $"EntityComponent '{GetType().Name}' on GameObject '{gameObject.name}' requires a MonoEntity in its parent hierarchy.");
                return _context;
            }
        }

        protected virtual void Awake()
        {
            EntityInjector.Invoke(gameObject);
            AspectInjector.Inject(Context, this);
        }

        /// <summary>
        /// Override to subscribe to aspect events. All subscriptions added to <paramref name="disposables"/>
        /// are automatically disposed when the component is disabled.
        /// </summary>
        protected virtual void OnSubscribe(ref DisposableBag disposables) { }

        protected virtual void OnEnable()
        {
            OnSubscribe(ref _disposables);
        }

        protected virtual void OnDisable()
        {
            _disposables.Dispose();
        }
    }
}
