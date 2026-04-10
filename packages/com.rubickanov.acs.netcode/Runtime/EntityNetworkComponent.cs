using R3;
using Rubickanov.ACS.Runtime;
using Unity.Netcode;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Base class for networked entity components. Override <see cref="OnSubscribe"/> to subscribe to aspect events;
    /// subscriptions are automatically disposed on <see cref="OnDisable"/> (including scope-disable from
    /// <c>AspectReplicator.ApplyNetworkScopes</c>, so a disabled component never fires stray reactions — regression #16).
    /// Mark aspect fields with <see cref="AspectAttribute"/> for automatic injection in Awake.
    /// </summary>
    public abstract class EntityNetworkComponent : NetworkBehaviour, IEntityComponent
    {
        private EntityContext? _context;
        private DisposableBag _disposables;
        private bool _subscribed;
        private bool _networkSpawned;

        protected EntityContext Context => _context ??= GetComponentInParent<EntityContext>();

        protected virtual void Awake()
        {
            EntityInjector.Inject?.Invoke(gameObject);
            AspectInjector.Inject(Context, this);
        }

        protected virtual void OnEnable() => TrySubscribe();

        protected virtual void OnDisable() => TryDispose();

        /// <summary>
        /// Override to subscribe to aspect events. All subscriptions added to <paramref name="disposables"/>
        /// are automatically disposed on <see cref="OnDisable"/>.
        /// </summary>
        protected virtual void OnSubscribe(ref DisposableBag disposables) { }

        public override void OnNetworkSpawn()
        {
            _networkSpawned = true;
            TrySubscribe();
        }

        public override void OnNetworkDespawn()
        {
            _networkSpawned = false;
            TryDispose();
        }

        // Gate for the two entry points (OnEnable / OnNetworkSpawn). Subscribe only once,
        // and only when the component is both network-spawned AND enabled — the latter check
        // is what closes the #16 race: if AspectReplicator scope-disabled us before our
        // OnNetworkSpawn fired, we must stay silent despite the spawn callback.
        private void TrySubscribe()
        {
            if (_subscribed) return;
            if (!_networkSpawned) return;
            if (!enabled) return;
            OnSubscribe(ref _disposables);
            _subscribed = true;
        }

        private void TryDispose()
        {
            if (!_subscribed) return;
            _disposables.Dispose();
            _disposables = default;
            _subscribed = false;
        }
    }
}
