using Unity.Netcode;

namespace Rubickanov.ACS.Runtime.Network
{
    /// <summary>
    /// Base class for networked entity components. Subscribe in OnNetworkSpawn, unsubscribe in OnNetworkDespawn.
    /// Obtain aspects via <c>Context.Require&lt;T&gt;()</c> in Awake.
    /// </summary>
    public abstract class EntityNetworkComponent : NetworkBehaviour, IEntityComponent
    {
        private EntityContext? _context;
        protected EntityContext Context => _context ??= GetComponentInParent<EntityContext>();

        protected virtual void Awake()
        {
            EntityInjector.Inject?.Invoke(gameObject);
        }
    }
}
