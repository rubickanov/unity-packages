using R3;
using Rubickanov.ACS.Runtime;
using Unity.Netcode;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Base class for networked entity components. Override <see cref="OnSubscribe"/> to subscribe to aspect events;
    /// subscriptions are automatically disposed on <see cref="OnNetworkDespawn"/>.
    /// Mark aspect fields with <see cref="AspectAttribute"/> for automatic injection in Awake.
    /// </summary>
    public abstract class EntityNetworkComponent : NetworkBehaviour, IEntityComponent
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
        /// are automatically disposed when the network object despawns.
        /// </summary>
        protected virtual void OnSubscribe(ref DisposableBag disposables) { }

        public override void OnNetworkSpawn()
        {
            OnSubscribe(ref _disposables);
        }

        public override void OnNetworkDespawn()
        {
            _disposables.Dispose();
        }
    }
}
