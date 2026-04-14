using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Base class for entity contexts that expose a scene-wide singleton reference.
    /// The first instance wins; a duplicate GameObject is destroyed during Awake so
    /// downstream subscribers never observe two live instances.
    /// </summary>
    public abstract class SingletonMonoEntity<T> : MonoEntity where T : SingletonMonoEntity<T>
    {
        /// <summary>
        /// The active singleton instance for this type, or <c>null</c> if none is alive in the scene.
        /// Assigned during <c>Awake</c> of the first instance; cleared in <c>OnDestroy</c>.
        /// </summary>
        public static T? Instance { get; private set; }

        // Reset the static Instance slot at the start of every play session. With Domain Reload
        // disabled in Project Settings → Enter Play Mode, OnDestroy from a prior session is not
        // guaranteed to null it (fast play-mode enter, cold exit on exception, asymmetric
        // scene-unload paths) — a stale reference survives into the next session and the first
        // Instance access returns a killed GameObject. Mirrors MonoEntity.ResetStaticEvents.
        //
        // [RuntimeInitializeOnLoadMethod] cannot live on a method in an open generic class —
        // Unity logs "methods cannot be in generic classes" and skips the hook. The dispatch
        // lives in SingletonMonoEntityResetter (non-generic): at SubsystemRegistration it walks
        // every concrete subclass of SingletonMonoEntity&lt;T&gt; via reflection and invokes this
        // method reflectively on each closed generic base. The test
        // SingletonMonoEntityTests.ResetInstanceOnPlayStart_WithLiveInstance_NullsInstance
        // pins the name "ResetInstanceOnPlayStart" so both reflection call sites stay in sync.
        private static void ResetInstanceOnPlayStart() => Instance = null;

        // A duplicate instance never becomes Instance, never registers aspects, and
        // never gets observed by Destroyed subscribers — so when Unity finally invokes
        // OnDestroy on its self-destroyed GameObject, we must skip the base lifecycle
        // (Destroyed event, World.Unregister) entirely. Otherwise subscribers who
        // happened to subscribe in the single frame between Awake and OnDestroy see
        // a Destroyed fire for an entity that was never alive to them.
        private bool _destroyedAsDuplicate;

        protected override void Awake()
        {
            if (Instance != null && Instance != this)
            {
                _destroyedAsDuplicate = true;
                Destroy(gameObject);
                return;
            }
            Instance = (T)this;
            base.Awake();
        }

        protected override void OnDestroy()
        {
            if (_destroyedAsDuplicate) return;
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }
    }
}
