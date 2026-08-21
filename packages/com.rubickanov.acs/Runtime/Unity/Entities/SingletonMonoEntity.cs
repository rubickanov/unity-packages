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
        // lives in SingletonMonoEntityResetter (non-generic), which invokes whatever resets
        // have registered themselves. The test
        // SingletonMonoEntityTests.ResetInstanceOnPlayStart_WithLiveInstance_NullsInstance
        // reaches this method by name via reflection, so keep the two in sync.
        private static void ResetInstanceOnPlayStart() => Instance = null;

        // Set once per closed T, never cleared: this flag and the resetter's list share one
        // static lifetime, so a session that finds the flag already true also finds its reset
        // still registered. Registering from Awake rather than a static constructor keeps
        // `Instance` free of a type-initializer check on every access.
        private static bool _resetHookRegistered;

        // A duplicate instance never becomes Instance, never registers aspects, and
        // never gets observed by Destroyed subscribers — so when Unity finally invokes
        // OnDestroy on its self-destroyed GameObject, we must skip the base lifecycle
        // (Destroyed event, World.Unregister) entirely. Otherwise subscribers who
        // happened to subscribe in the single frame between Awake and OnDestroy see
        // a Destroyed fire for an entity that was never alive to them.
        private bool _destroyedAsDuplicate;

        protected override void Awake()
        {
            if (!_resetHookRegistered)
            {
                _resetHookRegistered = true;
                SingletonMonoEntityResetter.Register(ResetInstanceOnPlayStart);
            }

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
            if (_destroyedAsDuplicate)
            {
                // The duplicate never became Instance, so we must not clear Instance here or
                // fire Destroyed (no subscriber saw it as alive). But we MUST still scrub any
                // aspects that a sibling EntityComponent on the same GameObject managed to
                // register between this.Awake and the deferred Destroy — otherwise those
                // aspects survive in World._registry's per-aspect index for the rest of the
                // session, and Query<T> iterates dead references.
                World.Current?.Unregister(this, AspectTypes);
                return;
            }
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }
    }
}
