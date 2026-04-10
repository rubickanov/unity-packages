using NUnit.Framework;
using R3;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class EntityNetworkComponentLifecycleTests
    {
        // ---- Fixture ------------------------------------------------------------
        //
        // EntityNetworkComponent is a NetworkBehaviour — normally spawned via NGO lifecycle,
        // which we cannot drive from edit-mode without a NetworkManager. We bypass the
        // lifecycle: AddComponent the test component on a bare GameObject with a NetworkObject,
        // then invoke OnNetworkSpawn / OnNetworkDespawn directly (they are public virtual).
        // The component tracks its own _networkSpawned state internally (instead of reading
        // NetworkBehaviour.IsSpawned), so no NetworkManager is needed.
        //
        // Important edit-mode quirk: Unity does NOT auto-invoke OnEnable/OnDisable when
        // `component.enabled` flips in edit mode (that's play-mode-only). Tests that need
        // to exercise the enable/disable lifecycle call InvokeOnEnable/InvokeOnDisable on
        // the test subclass, simulating what Unity would do at runtime. The `enabled` flag
        // itself is still flipped to a real value so TrySubscribe's guard sees reality.
        //
        // TestNetworkComponent overrides Awake() as a no-op to skip AspectInjector —
        // lifecycle is orthogonal to DI and injection is covered elsewhere.

        private GameObject _go;
        private TestNetworkComponent _component;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject(nameof(EntityNetworkComponentLifecycleTests));
            _go.AddComponent<NetworkObject>();
            _component = _go.AddComponent<TestNetworkComponent>();
            // Awake + OnEnable already fired; _networkSpawned == false at this point,
            // so TrySubscribe bails and SubscribeCount must still be zero.
            Assert.AreEqual(0, _component.SubscribeCount,
                "Subscribe must not fire before OnNetworkSpawn.");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        [Test]
        public void OnNetworkSpawn_WithComponentEnabled_InvokesOnSubscribeOnce()
        {
            _component.OnNetworkSpawn();

            Assert.AreEqual(1, _component.SubscribeCount);
            Assert.AreEqual(0, _component.DisposeCount);
        }

        [Test]
        public void SettingEnabledFalseAfterSpawn_DisposesSubscriptions()
        {
            _component.OnNetworkSpawn();

            _component.enabled = false;
            _component.InvokeOnDisable();

            Assert.AreEqual(1, _component.SubscribeCount);
            Assert.AreEqual(1, _component.DisposeCount);
        }

        [Test]
        public void ReEnablingAfterDisable_InvokesOnSubscribeAgain()
        {
            _component.OnNetworkSpawn();
            _component.enabled = false;
            _component.InvokeOnDisable();

            _component.enabled = true;
            _component.InvokeOnEnable();

            Assert.AreEqual(2, _component.SubscribeCount);
            Assert.AreEqual(1, _component.DisposeCount);
        }

        [Test]
        public void OnNetworkDespawn_WithActiveSubscription_DisposesOnce()
        {
            _component.OnNetworkSpawn();

            _component.OnNetworkDespawn();

            Assert.AreEqual(1, _component.SubscribeCount);
            Assert.AreEqual(1, _component.DisposeCount);
        }

        [Test]
        public void OnNetworkSpawn_WhenEnabledIsFalse_DoesNotSubscribe_RegressionSixteen()
        {
            // Simulates AspectReplicator.ApplyNetworkScopes synchronously disabling the
            // component before OnNetworkSpawn fires — scope-disable of a [ServerOnly]
            // component running on a pure client. Before #16, OnSubscribe would still fire
            // (subscribe was tied to OnNetworkSpawn), and R3 subscriptions ignored the
            // subsequent `enabled = false`, causing server-only logic to run on clients.
            _component.enabled = false;

            _component.OnNetworkSpawn();

            Assert.AreEqual(0, _component.SubscribeCount,
                "Scope-disabled component must not subscribe on OnNetworkSpawn (#16).");
            Assert.AreEqual(0, _component.DisposeCount);
        }

        private sealed class TestNetworkComponent : EntityNetworkComponent
        {
            public int SubscribeCount { get; private set; }
            public int DisposeCount { get; private set; }

            // Skip base Awake — AspectInjector requires an EntityContext that we do not
            // wire up for lifecycle tests. Injection is covered by dedicated DI tests.
            protected override void Awake() { }

            // Manual proxies for the protected lifecycle hooks. See the fixture comment:
            // edit-mode tests need to invoke these by hand because Unity only fires them
            // on enabled-flag transitions in play mode.
            public void InvokeOnEnable() => OnEnable();
            public void InvokeOnDisable() => OnDisable();

            protected override void OnSubscribe(ref DisposableBag disposables)
            {
                SubscribeCount++;
                Disposable.Create(() => DisposeCount++).AddTo(ref disposables);
            }
        }
    }
}
