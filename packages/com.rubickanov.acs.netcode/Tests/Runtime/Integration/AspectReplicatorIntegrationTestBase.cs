using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Runtime.Netcode.Tests.Integration
{
    /// <summary>
    /// Base fixture for AspectReplicator integration tests. Spins up one host and
    /// <see cref="NumberOfClients"/> pure clients on a UnityTransport loopback
    /// connection, registers the test prefabs declared by subclasses, and exposes
    /// the helper surface the test suites rely on.
    ///
    /// This fixture is deliberately self-contained: it does NOT inherit from
    /// <c>NetcodeIntegrationTest</c>, so the package's test asmdef does not need
    /// to reference <c>Unity.Netcode.Runtime.Tests</c> — which would require
    /// making NGO itself testable, pulling hundreds of NGO's own tests into the
    /// project's Test Runner. The helper signatures mirror the subset of
    /// <c>NetcodeIntegrationTest</c> used by the test suites below so individual
    /// tests read the same as they would against NGO's fixture.
    /// </summary>
    public abstract class AspectReplicatorIntegrationTestBase
    {
        // ---- Fixture config ------------------------------------------------
        //
        // Host + 2 pure clients is the minimum that makes owner-auth tests
        // observable: a third peer is needed to see "owner writes → server
        // relay → other client receives". Subclasses may override.

        protected virtual int NumberOfClients => 2;

        // Loopback wiring. Fixed port is safe because tests run serially and
        // the teardown fully shuts down the previous NetworkManagers before
        // the next [UnitySetUp] binds again. Clients use ephemeral outbound
        // ports (UnityTransport's ClientBindPort defaults to 0).
        private const string k_LoopbackAddress = "127.0.0.1";
        private const ushort k_Port = 17777;

        // Connection / spawn waits: 4 seconds at 30 Hz = ~120 ticks. Long
        // enough to swallow first-frame stalls in the editor, short enough
        // that a broken test fails fast instead of hanging the runner.
        private const float k_DefaultTimeout = 4f;

        // Match NGO's NetcodeIntegrationTest.s_DefaultWaitForTick exactly so
        // `yield return s_DefaultWaitForTick` in suites pumps one NGO tick.
        protected static readonly WaitForSecondsRealtime s_DefaultWaitForTick = new(1f / 30f);

        // Unique GlobalObjectIdHash counter for runtime-created test prefabs.
        // A zero hash is rejected by NetworkConfig.Prefabs.Add; Unity normally
        // assigns these in-editor from the asset GUID, but we're creating
        // GameObjects at runtime so we have to fabricate them ourselves. Start
        // above the range Unity itself picks to avoid any collision with a
        // genuine imported prefab that might also be in the scene.
        private static uint s_NextPrefabHash = 0x10000000;

        // ---- Reflection caches ---------------------------------------------

        private static readonly FieldInfo s_GlobalObjectIdHashField =
            typeof(NetworkObject).GetField("GlobalObjectIdHash",
                BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"Cannot find NetworkObject.GlobalObjectIdHash — NGO internal field renamed?");

        // NetworkManagerOwner went from public-setter to `internal` in NGO 2.x,
        // so the test fixture can no longer assign it directly. SpawnObject
        // still has to bind a runtime-created NetworkObject to the authority's
        // NetworkManager before Spawn(), so we poke it via reflection the same
        // way we do with GlobalObjectIdHash above.
        private static readonly FieldInfo s_NetworkManagerOwnerField =
            typeof(NetworkObject).GetField("NetworkManagerOwner",
                BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"Cannot find NetworkObject.NetworkManagerOwner — NGO internal field renamed?");

        // ---- Network manager handles --------------------------------------
        //
        // Mirrors the field names used by NetcodeIntegrationTest so the suite
        // code reads identically against either base class.

        protected NetworkManager m_ServerNetworkManager = null!;
        protected NetworkManager[] m_ClientNetworkManagers = Array.Empty<NetworkManager>();

        /// <summary>
        /// Host + all pure clients, in index order. Tests iterate this when
        /// they need to assert an invariant on every peer.
        /// </summary>
        protected NetworkManager[] m_NetworkManagers = Array.Empty<NetworkManager>();

        // ---- Test prefabs --------------------------------------------------
        //
        // Created in OnServerAndClientsCreated (before NetworkManagers start)
        // and reused across every test in the suite. Each prefab targets a
        // single concern so individual tests do not pay the cost of standing
        // up unrelated components.

        /// <summary>NetworkObject + MonoEntity + AspectReplicator + StateTestAspectRegistrar.</summary>
        protected GameObject _statePrefab = null!;

        /// <summary>NetworkObject + MonoEntity + AspectReplicator + EventTestAspectRegistrar.</summary>
        protected GameObject _eventPrefab = null!;

        /// <summary>State prefab + ServerOnly + OwnerOnly marker components for scope tests.</summary>
        protected GameObject _scopePrefab = null!;

        /// <summary>NetworkObject + AspectReplicator (no MonoEntity) — regression #13/#15 fixture.</summary>
        protected GameObject _brokenContextPrefab = null!;

        /// <summary>Parent NetworkObject + scope marker, with a child NetworkObject also carrying a scope marker — regression #3 fixture.</summary>
        protected GameObject _nestedScopePrefab = null!;

        /// <summary>NetworkObject + MonoEntity + AspectReplicator + MonsterStateAspectRegistrar (65 fields, one over the 64-field cap) — regression #2 fixture.</summary>
        protected GameObject _monsterPrefab = null!;

        // Prefabs created via CreateNetworkObjectPrefab, tracked so teardown
        // can destroy their source GameObjects and so CreateAndStartNewClient
        // can re-register them on a late joiner's NetworkConfig.Prefabs.
        private readonly List<GameObject> m_TrackedPrefabs = new();

        // Set by WaitForConditionOrTimeOut when the predicate never went true
        // inside the timeout window. AssertOnTimeout reads this.
        private bool m_LastWaitTimedOut;

        // ---- Subclass hook -------------------------------------------------

        /// <summary>
        /// Called once, after NetworkManagers exist but before any of them start.
        /// Subclasses create prefabs via <see cref="CreateNetworkObjectPrefab"/>
        /// here — the prefab registration step requires all NetworkManagers to be
        /// present but idle.
        /// </summary>
        protected virtual void OnServerAndClientsCreated()
        {
            _statePrefab = CreateNetworkObjectPrefab("StateEntity");
            _statePrefab.AddComponent<MonoEntity>();
            _statePrefab.AddComponent<AspectReplicator>();
            _statePrefab.AddComponent<StateTestAspectRegistrar>();

            _eventPrefab = CreateNetworkObjectPrefab("EventEntity");
            _eventPrefab.AddComponent<MonoEntity>();
            _eventPrefab.AddComponent<AspectReplicator>();
            _eventPrefab.AddComponent<EventTestAspectRegistrar>();

            _scopePrefab = CreateNetworkObjectPrefab("ScopeEntity");
            _scopePrefab.AddComponent<MonoEntity>();
            _scopePrefab.AddComponent<AspectReplicator>();
            _scopePrefab.AddComponent<StateTestAspectRegistrar>();
            _scopePrefab.AddComponent<ServerOnlyMarkerComponent>();
            _scopePrefab.AddComponent<OwnerOnlyMarkerComponent>();

            // No MonoEntity: AspectReplicator must log an error and bail
            // gracefully without an NRE on OnNetworkSpawn. Regression #13/#15.
            _brokenContextPrefab = CreateNetworkObjectPrefab("BrokenContextEntity");
            _brokenContextPrefab.AddComponent<AspectReplicator>();

            // Nested NetworkObject: ApplyNetworkScopes on the parent must stop
            // walking children at the inner NetworkObject boundary, so the
            // child's scope component is *not* governed by the parent's
            // replicator. Regression #3.
            _nestedScopePrefab = CreateNetworkObjectPrefab("NestedScopeParentEntity");
            _nestedScopePrefab.AddComponent<MonoEntity>();
            _nestedScopePrefab.AddComponent<AspectReplicator>();
            _nestedScopePrefab.AddComponent<ServerOnlyMarkerComponent>();

            var nestedChild = new GameObject("NestedScopeChild");
            nestedChild.transform.SetParent(_nestedScopePrefab.transform);
            // Child has its OWN NetworkObject — that's what makes the boundary
            // visible to ApplyNetworkScopes. Without this, the child's scope
            // component would be swept up by the parent's GetComponentsInChildren.
            var childNetworkObject = nestedChild.AddComponent<NetworkObject>();
            AssignPrefabHash(childNetworkObject);
            childNetworkObject.SetSceneObjectStatus(false);
            nestedChild.AddComponent<ServerOnlyMarkerComponent>();

            // 65-field aspect: triggers the > 64 clamp path inside
            // AspectReplicator.OnNetworkSpawn. Regression #2.
            _monsterPrefab = CreateNetworkObjectPrefab("MonsterEntity");
            _monsterPrefab.AddComponent<MonoEntity>();
            _monsterPrefab.AddComponent<AspectReplicator>();
            _monsterPrefab.AddComponent<MonsterStateAspectRegistrar>();
        }

        // ---- Lifecycle -----------------------------------------------------

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_ServerNetworkManager = CreateNetworkManager("Server", isServer: true);
            m_ClientNetworkManagers = new NetworkManager[NumberOfClients];
            for (int i = 0; i < NumberOfClients; i++)
            {
                m_ClientNetworkManagers[i] = CreateNetworkManager($"Client-{i}", isServer: false);
            }
            RebuildCombinedManagers();

            OnServerAndClientsCreated();

            Assert.IsTrue(m_ServerNetworkManager.StartHost(),
                "StartHost() returned false — loopback port already bound?");
            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
            {
                Assert.IsTrue(m_ClientNetworkManagers[i].StartClient(),
                    $"StartClient() on client {i} returned false.");
            }

            // The server fires IsConnectedClient immediately because StartHost
            // wires a host-local ClientId 0 without a real handshake. Pure
            // clients need a few loopback ticks to complete the NGO
            // handshake and become IsConnectedClient.
            yield return WaitForConditionOrTimeOut(() =>
            {
                for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
                    if (!m_ClientNetworkManagers[i].IsConnectedClient) return false;
                return m_ServerNetworkManager.IsListening
                       && m_ServerNetworkManager.ConnectedClients.Count == m_ClientNetworkManagers.Length + 1;
            });
            AssertOnTimeout("Host + clients did not finish handshake within the default timeout.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Shut down clients first so the server sees orderly disconnects,
            // then the server. Destroy GameObjects afterwards so any lingering
            // NGO callbacks run against a still-valid NetworkManager.
            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
            {
                if (m_ClientNetworkManagers[i] != null)
                {
                    m_ClientNetworkManagers[i].Shutdown();
                }
            }
            if (m_ServerNetworkManager != null)
            {
                m_ServerNetworkManager.Shutdown();
            }

            // Give NGO a frame to flush its shutdown queue before the
            // GameObjects disappear out from under it.
            yield return null;

            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
            {
                if (m_ClientNetworkManagers[i] != null)
                    UnityEngine.Object.DestroyImmediate(m_ClientNetworkManagers[i].gameObject);
            }
            if (m_ServerNetworkManager != null)
                UnityEngine.Object.DestroyImmediate(m_ServerNetworkManager.gameObject);

            for (int i = 0; i < m_TrackedPrefabs.Count; i++)
            {
                if (m_TrackedPrefabs[i] != null)
                    UnityEngine.Object.DestroyImmediate(m_TrackedPrefabs[i]);
            }
            m_TrackedPrefabs.Clear();

            m_ServerNetworkManager = null!;
            m_ClientNetworkManagers = Array.Empty<NetworkManager>();
            m_NetworkManagers = Array.Empty<NetworkManager>();
            m_LastWaitTimedOut = false;
        }

        // ---- NetworkManager creation --------------------------------------

        private NetworkManager CreateNetworkManager(string suffix, bool isServer)
        {
            var go = new GameObject($"NetworkManager - {suffix}");
            var networkManager = go.AddComponent<NetworkManager>();
            var transport = go.AddComponent<UnityTransport>();
            transport.SetConnectionData(k_LoopbackAddress, k_Port, isServer ? k_LoopbackAddress : null);

            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.TickRate = 30;
            // Scene management disabled: our tests spawn everything manually via
            // SpawnObject. With it enabled NGO would try to synchronize the test
            // scene on client connect and fail on any in-scene NetworkObjects
            // that lack a valid GlobalObjectIdHash.
            networkManager.NetworkConfig.EnableSceneManagement = false;
            // Explicitly no player prefab — the suites spawn their own entities
            // via SpawnObject and do not exercise player-prefab auto-spawn.
            networkManager.NetworkConfig.PlayerPrefab = null;
            return networkManager;
        }

        private void RebuildCombinedManagers()
        {
            m_NetworkManagers = new NetworkManager[1 + m_ClientNetworkManagers.Length];
            m_NetworkManagers[0] = m_ServerNetworkManager;
            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
                m_NetworkManagers[i + 1] = m_ClientNetworkManagers[i];
        }

        // ---- Prefab creation / spawning ------------------------------------

        /// <summary>
        /// Creates a runtime GameObject configured as a NetworkPrefab and
        /// registers it with every existing NetworkManager's
        /// <see cref="NetworkConfig.Prefabs"/>. Must be called AFTER the
        /// NetworkManagers are created but BEFORE they start.
        /// </summary>
        protected GameObject CreateNetworkObjectPrefab(string baseName)
        {
            var go = new GameObject(baseName);
            var networkObject = go.AddComponent<NetworkObject>();
            AssignPrefabHash(networkObject);
            networkObject.SetSceneObjectStatus(false);

            RegisterPrefabWithAllManagers(go);
            m_TrackedPrefabs.Add(go);

            return go;
        }

        private static void AssignPrefabHash(NetworkObject networkObject)
        {
            // GlobalObjectIdHash is `internal uint` — normally populated by
            // Unity's prefab import pipeline from the asset GUID. Runtime-
            // created templates never go through that pipeline, so we stamp a
            // unique hash via reflection instead.
            s_GlobalObjectIdHashField.SetValue(networkObject, s_NextPrefabHash++);
        }

        private void RegisterPrefabWithAllManagers(GameObject prefab)
        {
            for (int i = 0; i < m_NetworkManagers.Length; i++)
            {
                m_NetworkManagers[i].NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = prefab });
            }
        }

        /// <summary>
        /// Instantiates the given prefab GameObject and spawns it on the
        /// <paramref name="owner"/> NetworkManager. Returns the server-side
        /// instance (never the source prefab itself).
        /// </summary>
        protected GameObject SpawnObject(GameObject prefab, NetworkManager owner)
        {
            var prefabNetworkObject = prefab.GetComponent<NetworkObject>();
            Assert.IsNotNull(prefabNetworkObject,
                $"{prefab.name} does not have a NetworkObject — did you create it via CreateNetworkObjectPrefab?");
            Assert.IsTrue(owner.IsServer,
                "SpawnObject expects a server/host NetworkManager — NGO requires spawns to happen on the authority.");

            var instance = UnityEngine.Object.Instantiate(prefab);
            var instanceNetworkObject = instance.GetComponent<NetworkObject>();
            s_NetworkManagerOwnerField.SetValue(instanceNetworkObject, owner);
            instanceNetworkObject.Spawn(destroyWithScene: false);
            return instance;
        }

        // ---- Late-join client ----------------------------------------------

        /// <summary>
        /// Creates a new NetworkManager, registers the already-created test
        /// prefabs with it, starts it as a client, and waits for the handshake
        /// to complete. On success <see cref="m_ClientNetworkManagers"/> and
        /// <see cref="m_NetworkManagers"/> are expanded to include the new peer.
        /// </summary>
        protected IEnumerator CreateAndStartNewClient()
        {
            var lateJoiner = CreateNetworkManager($"Client-{m_ClientNetworkManagers.Length}", isServer: false);
            // Replay the already-registered prefabs onto the new client so its
            // SpawnedObjects lookups match the existing peers.
            for (int i = 0; i < m_TrackedPrefabs.Count; i++)
            {
                lateJoiner.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = m_TrackedPrefabs[i] });
            }

            var expandedClients = new NetworkManager[m_ClientNetworkManagers.Length + 1];
            Array.Copy(m_ClientNetworkManagers, expandedClients, m_ClientNetworkManagers.Length);
            expandedClients[^1] = lateJoiner;
            m_ClientNetworkManagers = expandedClients;
            RebuildCombinedManagers();

            Assert.IsTrue(lateJoiner.StartClient(),
                "StartClient() on late-joining client returned false.");

            yield return WaitForConditionOrTimeOut(() => lateJoiner.IsConnectedClient);
            AssertOnTimeout("Late-joining client did not complete handshake within the default timeout.");
        }

        // ---- Wait helpers --------------------------------------------------

        /// <summary>
        /// Yields until <paramref name="condition"/> returns true or the
        /// default timeout elapses. On timeout <see cref="AssertOnTimeout"/>
        /// is the follow-up call that actually fails the test — mirrors NGO's
        /// two-step wait-then-assert pattern.
        /// </summary>
        protected IEnumerator WaitForConditionOrTimeOut(Func<bool> condition)
        {
            m_LastWaitTimedOut = false;
            var deadline = Time.realtimeSinceStartup + k_DefaultTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (condition()) yield break;
                yield return s_DefaultWaitForTick;
            }
            m_LastWaitTimedOut = !condition();
        }

        /// <summary>
        /// Asserts the most recent <see cref="WaitForConditionOrTimeOut"/>
        /// finished by the predicate going true. Invoke immediately after the
        /// wait so the flag is still fresh.
        /// </summary>
        protected void AssertOnTimeout(string message)
        {
            Assert.IsFalse(m_LastWaitTimedOut, message);
        }

        // ---- Per-client lookups --------------------------------------------
        //
        // Each NetworkManager owns its own SpawnManager, and the spawned
        // instance of an entity on each peer is a *different* GameObject — so
        // tests must always look up the peer-local component, never reuse the
        // server's reference. Helpers below centralize that lookup.

        /// <summary>
        /// Returns the <see cref="AspectReplicator"/> instance that lives on
        /// <paramref name="client"/>'s copy of the entity with the given id.
        /// </summary>
        protected static AspectReplicator GetReplicatorOnClient(NetworkManager client, ulong networkObjectId)
        {
            Assert.IsTrue(
                client.SpawnManager.SpawnedObjects.ContainsKey(networkObjectId),
                $"NetworkObject {networkObjectId} is not spawned on client {client.LocalClientId}.");
            return client.SpawnManager.SpawnedObjects[networkObjectId].GetComponent<AspectReplicator>();
        }

        /// <summary>
        /// Returns the local <see cref="StateTestAspect"/> stored in
        /// <paramref name="client"/>'s <see cref="MonoEntity"/> for the
        /// given entity. Each peer creates its own aspect instance — never
        /// share aspect references across NetworkManagers.
        /// </summary>
        protected static StateTestAspect GetStateAspectOnClient(NetworkManager client, ulong networkObjectId)
        {
            var go = client.SpawnManager.SpawnedObjects[networkObjectId].gameObject;
            return go.GetComponent<MonoEntity>().Require<StateTestAspect>();
        }

        /// <summary>
        /// Returns the local <see cref="EventTestAspect"/> stored in
        /// <paramref name="client"/>'s <see cref="MonoEntity"/>.
        /// </summary>
        protected static EventTestAspect GetEventAspectOnClient(NetworkManager client, ulong networkObjectId)
        {
            var go = client.SpawnManager.SpawnedObjects[networkObjectId].gameObject;
            return go.GetComponent<MonoEntity>().Require<EventTestAspect>();
        }

        /// <summary>
        /// Waits until the entity with id <paramref name="networkObjectId"/>
        /// has been spawned on every client in <see cref="m_NetworkManagers"/>.
        /// Asserts on timeout — keeps tests from silently passing when the
        /// spawn message never lands.
        /// </summary>
        protected IEnumerator WaitForSpawnOnAllClients(ulong networkObjectId)
        {
            yield return WaitForConditionOrTimeOut(() =>
            {
                for (int i = 0; i < m_NetworkManagers.Length; i++)
                {
                    if (!m_NetworkManagers[i].SpawnManager.SpawnedObjects.ContainsKey(networkObjectId))
                        return false;
                }
                return true;
            });
            AssertOnTimeout($"Timed out waiting for NetworkObject {networkObjectId} to spawn on all clients.");
        }
    }
}
