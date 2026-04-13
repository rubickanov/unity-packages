using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Runtime.Netcode.Tests.Integration
{
    /// <summary>
    /// Integration coverage for the OnNetworkSpawn → bindings init → tick →
    /// OnNetworkDespawn lifecycle of <see cref="AspectReplicator"/>, driven
    /// through real NGO spawn/despawn on a host + 2 clients fixture.
    ///
    /// Pure-unit replicator tests live alongside in
    /// <c>ApplyStateBufferRoundTripTests</c> and friends; this suite exercises
    /// the parts that only show up when a real NetworkManager is in the loop.
    /// </summary>
    public class AspectReplicatorLifecycleTests : AspectReplicatorIntegrationTestBase
    {
        [UnityTest]
        public IEnumerator Spawn_WithValidContext_BindingsCreatedOnAllClients()
        {
            // Sanity baseline: a happy-path spawn must end with each peer
            // holding two bindings (StateTestAspect.OwnerValue + ServerValue)
            // — anything less means the scan or registrar wiring is broken.
            var serverInstance = SpawnObject(_statePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;

            yield return WaitForSpawnOnAllClients(networkObjectId);

            for (int i = 0; i < m_NetworkManagers.Length; i++)
            {
                var replicator = GetReplicatorOnClient(m_NetworkManagers[i], networkObjectId);
                Assert.AreEqual(2, GetBindingCount(replicator),
                    $"Client {m_NetworkManagers[i].LocalClientId} must have 2 bindings (one server-auth, one owner-auth).");
            }
        }

        [UnityTest]
        public IEnumerator Spawn_WithoutMonoEntity_LogsErrorAndDoesNotNRE_RegressionThirteenFifteen()
        {
            // Regression #13/#15: a misconfigured prefab missing MonoEntity
            // must produce ONE clear error and not crash with a NullReferenceException.
            // Before the fix, OnNetworkSpawn dereferenced the null context inside
            // the scan loop and tore down NGO with an unhandled exception, taking
            // the rest of the spawn pipeline with it.
            //
            // The error fires once per peer that runs OnNetworkSpawn — server +
            // both clients = 3 instances. Use a regex so the same Expect call
            // covers every peer's instance name without coupling to the
            // hash-suffixed NGO name.
            for (int i = 0; i < m_NetworkManagers.Length; i++)
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                    @"\[AspectReplicator\] '.*' is missing MonoEntity"));
            }

            var serverInstance = SpawnObject(_brokenContextPrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;

            yield return WaitForSpawnOnAllClients(networkObjectId);

            // No NRE, no extra logs. The replicator must end up in a quiescent
            // state with zero bindings — exactly what a graceful bail looks like.
            for (int i = 0; i < m_NetworkManagers.Length; i++)
            {
                var replicator = GetReplicatorOnClient(m_NetworkManagers[i], networkObjectId);
                Assert.AreEqual(0, GetBindingCount(replicator),
                    "Replicator with no MonoEntity must produce zero bindings, not crash mid-scan.");
            }
        }

        [UnityTest]
        public IEnumerator Despawn_TearsDownAllSubscriptions_NoOrphanCallbacks()
        {
            // After OnNetworkDespawn the replicator must stop relaying state.
            // The scenario: server writes once → all clients converge → server
            // despawns the entity → server writes to its (now-orphaned) aspect
            // → no client should see the second value, because the despawn
            // disposed the authority subscription. Without that disposal,
            // ClearDirty would still be called and the next tick would
            // attempt to RPC to clients that no longer have the entity.
            var serverInstance = SpawnObject(_statePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            var serverAspect = GetStateAspectOnClient(m_ServerNetworkManager, networkObjectId);
            serverAspect.ServerValue.Value = 100;

            // Wait for both clients to apply the first write before despawn.
            yield return WaitForConditionOrTimeOut(() =>
            {
                for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
                {
                    if (GetStateAspectOnClient(m_ClientNetworkManagers[i], networkObjectId).ServerValue.Value != 100)
                        return false;
                }
                return true;
            });
            AssertOnTimeout("Initial server write did not propagate to clients before despawn.");

            // Capture each client's aspect reference BEFORE despawn — after
            // despawn the SpawnedObjects entry is gone and the lookup helper
            // would throw. The aspect instance itself outlives despawn because
            // the GameObject is still around briefly.
            var clientAspects = new StateTestAspect[m_ClientNetworkManagers.Length];
            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
                clientAspects[i] = GetStateAspectOnClient(m_ClientNetworkManagers[i], networkObjectId);

            serverInstance.GetComponent<NetworkObject>().Despawn(destroy: true);

            // The aspect on the server side still exists (we held a reference)
            // but is no longer subscribed by the replicator's authority hook.
            // Writing to it should be a no-op as far as the network is concerned.
            // Use the same instance we captured before despawn — the GameObject
            // may be destroyed but the aspect object's local Value setter still
            // works because R3 ReactiveProperty has no dependence on Unity.
            serverAspect.ServerValue.Value = 999;

            // Spin a few ticks so any orphaned tick callback would get a
            // chance to run. Then assert no client moved past 100.
            for (int i = 0; i < 5; i++) yield return s_DefaultWaitForTick;

            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
            {
                Assert.AreEqual(100, clientAspects[i].ServerValue.Value,
                    $"Client {m_ClientNetworkManagers[i].LocalClientId} received a post-despawn write — subscriptions were not torn down.");
            }
        }

        [UnityTest]
        public IEnumerator Spawn_WithSixtyFiveFields_AllBindingsCreated()
        {
            // MonsterStateAspect has 65 [Replicated] fields. With the
            // variable-length byte[] mask (replacing the old ulong), all 65
            // must be scanned and bound — no clamp, no error. The cap is now
            // 256 fields.
            var serverInstance = SpawnObject(_monsterPrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            for (int i = 0; i < m_NetworkManagers.Length; i++)
            {
                var replicator = GetReplicatorOnClient(m_NetworkManagers[i], networkObjectId);
                Assert.AreEqual(65, GetBindingCount(replicator),
                    $"Client {m_NetworkManagers[i].LocalClientId} must have all 65 bindings from MonsterStateAspect.");
            }
        }

        // ---- Reflection helpers --------------------------------------------

        private static int GetBindingCount(AspectReplicator replicator)
        {
            var field = typeof(AspectReplicator).GetField("_bindings",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "_bindings field renamed?");
            var arr = (ReplicatedFieldBinding[])field!.GetValue(replicator)!;
            return arr.Length;
        }
    }
}
