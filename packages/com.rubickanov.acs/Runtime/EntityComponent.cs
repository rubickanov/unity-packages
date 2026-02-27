using UnityEngine;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Base class for entity components. Subscribe in OnEnable, unsubscribe in OnDisable.
    /// Obtain aspects via <c>Context.Require&lt;T&gt;()</c> in Awake.
    /// </summary>
    public abstract class EntityComponent : MonoBehaviour, IEntityComponent
    {
        private EntityContext? _context;
        protected EntityContext Context => _context ??= GetComponentInParent<EntityContext>();

        protected virtual void Awake()
        {
            EntityInjector.Inject?.Invoke(gameObject);
        }
    }
}
