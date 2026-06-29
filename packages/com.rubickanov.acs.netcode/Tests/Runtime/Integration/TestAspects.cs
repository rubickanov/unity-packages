using R3;
using Rubickanov.ACS.Runtime;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests.Integration
{
    // ---- Test aspects --------------------------------------------------------
    //
    // Minimal aspects shared by every integration suite. Two reactive fields per
    // aspect — one server-auth, one owner-auth — exercise both authority paths
    // through a single spawn so suites do not need their own type zoo.

    /// <summary>
    /// Aspect with one server-auth and one owner-auth replicated state field.
    /// Used by lifecycle, state-sync, owner-auth and scope suites.
    /// </summary>
    public sealed class StateTestAspect : IEntityAspect
    {
        [Replicated]
        public ReactiveProperty<int> ServerValue = new(0);

        [Replicated(Authority = AuthorityMode.Owner)]
        public ReactiveProperty<float> OwnerValue = new(0f);
    }

    /// <summary>
    /// Aspect with one server-auth and one owner-auth replicated event field.
    /// Used by the event suite.
    /// </summary>
    public sealed class EventTestAspect : IEntityAspect
    {
        [ReplicatedEvent]
        public Subject<int> ServerEvent = new();

        [ReplicatedEvent(Authority = AuthorityMode.Owner)]
        public Subject<int> OwnerEvent = new();
    }

    // ---- Aspect registrars ---------------------------------------------------
    //
    // MonoEntity creates aspects lazily on Require<T>(). EntityReplicator's
    // OnNetworkSpawn iterates context.GetAllAspects() — so unless something has
    // already touched the aspect via Require<T>() before then, the replicator
    // sees an empty bag and binds zero fields. The registrars below run in
    // Awake() and do the Require<T>() call so the aspect is materialized
    // before NGO drives OnNetworkSpawn.

    /// <summary>
    /// Forces <see cref="StateTestAspect"/> creation in Awake so the replicator
    /// can scan it during OnNetworkSpawn.
    /// </summary>
    public sealed class StateTestAspectRegistrar : MonoBehaviour, IEntityComponent
    {
        [System.NonSerialized] public StateTestAspect Aspect = default!;

        private void Awake()
        {
            var context = GetComponentInParent<MonoEntity>();
            Aspect = context.Require<StateTestAspect>();
        }
    }

    /// <summary>
    /// Forces <see cref="EventTestAspect"/> creation in Awake so the replicator
    /// can scan it during OnNetworkSpawn.
    /// </summary>
    public sealed class EventTestAspectRegistrar : MonoBehaviour, IEntityComponent
    {
        [System.NonSerialized] public EventTestAspect Aspect = default!;

        private void Awake()
        {
            var context = GetComponentInParent<MonoEntity>();
            Aspect = context.Require<EventTestAspect>();
        }
    }

    // ---- Scope marker components --------------------------------------------
    //
    // EntityNetworkComponent subclasses with [NetworkScope] attributes — used
    // by scope tests to verify ApplyNetworkScopes disables them on the right
    // peers. They also count OnSubscribe invocations so a test can assert that
    // a scope-disabled component never subscribes (regression #16).

    /// <summary>
    /// EntityNetworkComponent that may only run on the server. Used by scope
    /// tests to verify <c>ApplyNetworkScopes</c> disables it on pure clients.
    /// </summary>
    [NetworkScope(NetworkScope.ServerOnly)]
    public sealed class ServerOnlyMarkerComponent : EntityNetworkComponent
    {
        public int SubscribeCount { get; private set; }

        // Skip AspectInjector — these markers do not consume aspects, and the
        // base Awake would otherwise force the test prefab to also carry an
        // MonoEntity just for injection bookkeeping.
        protected override void Awake() { }

        protected override void OnSubscribe(ref DisposableBag disposables)
        {
            SubscribeCount++;
        }
    }

    /// <summary>
    /// EntityNetworkComponent that may only run on the owning client. Used by
    /// scope tests to verify <c>ApplyNetworkScopes</c> + <c>ReapplyOwnerScope</c>
    /// flip the enabled flag on ownership transfer.
    /// </summary>
    [NetworkScope(NetworkScope.OwnerOnly)]
    public sealed class OwnerOnlyMarkerComponent : EntityNetworkComponent
    {
        public int SubscribeCount { get; private set; }

        protected override void Awake() { }

        protected override void OnSubscribe(ref DisposableBag disposables)
        {
            SubscribeCount++;
        }
    }
}
