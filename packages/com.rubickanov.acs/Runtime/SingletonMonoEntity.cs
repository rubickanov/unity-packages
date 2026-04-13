using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Base class for entity contexts that expose a scene-wide singleton reference.
    /// The first instance wins; a duplicate GameObject is destroyed during Awake so
    /// downstream subscribers never observe two live instances.
    /// </summary>
    [MovedFrom(true, sourceNamespace: "Rubickanov.ACS.Runtime", sourceAssembly: "ACS.Runtime", sourceClassName: "SingletonEntityContext`1")]
    public abstract class SingletonMonoEntity<T> : MonoEntity where T : SingletonMonoEntity<T>
    {
        /// <summary>
        /// The active singleton instance for this type, or <c>null</c> if none is alive in the scene.
        /// Assigned during <c>Awake</c> of the first instance; cleared in <c>OnDestroy</c>.
        /// </summary>
        public static T? Instance { get; private set; }

        protected override void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = (T)this;
            base.Awake();
        }

        protected override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }
    }
}
