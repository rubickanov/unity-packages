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
        private EntityContext? _context;
        private DisposableBag _disposables;

        protected EntityContext Context => _context ??= GetComponentInParent<EntityContext>();

        protected virtual void Awake()
        {
            EntityInjector.Inject?.Invoke(gameObject);
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
