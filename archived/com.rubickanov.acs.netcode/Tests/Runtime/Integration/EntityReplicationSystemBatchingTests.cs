using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Runtime.Netcode.Tests.Integration
{
    /// <summary>
    /// Integration coverage for <see cref="EntityReplicationSystem"/> centralized
    /// batching: multi-entity dirty batching, zero-dirty skip, and
    /// spawn/despawn lifecycle cleanup.
    /// </summary>
    public class EntityReplicationSystemBatchingTests : EntityReplicatorIntegrationTestBase
    {
        [UnityTest]
        public IEnumerator TwoDirtyReplicators_OneTickWindow_BothClientsConverge()
        {
            var serverA = SpawnObject(_statePrefab, m_ServerNetworkManager);
            var idA = serverA.GetComponent<NetworkObject>().NetworkObjectId;
            var serverB = SpawnObject(_statePrefab, m_ServerNetworkManager);
            var idB = serverB.GetComponent<NetworkObject>().NetworkObjectId;

            yield return WaitForSpawnOnAllClients(idA);
            yield return WaitForSpawnOnAllClients(idB);

            GetStateAspectOnClient(m_ServerNetworkManager, idA).ServerValue.Value = 10;
            GetStateAspectOnClient(m_ServerNetworkManager, idB).ServerValue.Value = 20;

            yield return WaitForConditionOrTimeOut(() =>
            {
                for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
                {
                    if (GetStateAspectOnClient(m_ClientNetworkManagers[i], idA).ServerValue.Value != 10)
                        return false;
                    if (GetStateAspectOnClient(m_ClientNetworkManagers[i], idB).ServerValue.Value != 20)
                        return false;
                }
                return true;
            });
            AssertOnTimeout("Two dirty entities written in the same tick window must converge on all clients.");
        }

        [UnityTest]
        public IEnumerator NoDirtyReplicators_NoStateChangeOnClients()
        {
            var serverInstance = SpawnObject(_statePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            GetStateAspectOnClient(m_ServerNetworkManager, networkObjectId).ServerValue.Value = 42;

            yield return WaitForConditionOrTimeOut(() =>
            {
                for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
                {
                    if (GetStateAspectOnClient(m_ClientNetworkManagers[i], networkObjectId).ServerValue.Value != 42)
                        return false;
                }
                return true;
            });
            AssertOnTimeout("Initial write must propagate.");

            // No further writes on the server. Spin several ticks — clients
            // must stay at 42. If the system were sending spurious batches or
            // re-applying stale data, this would drift.
            for (int i = 0; i < 10; i++) yield return s_DefaultWaitForTick;

            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
            {
                Assert.AreEqual(42,
                    GetStateAspectOnClient(m_ClientNetworkManagers[i], networkObjectId).ServerValue.Value,
                    $"Client {m_ClientNetworkManagers[i].LocalClientId} must not receive phantom updates when nothing is dirty.");
            }
        }

        [UnityTest]
        public IEnumerator SpawnDespawnRepeatedly_SystemReplicatorCountZero()
        {
            // Spawn three entities, despawn all, verify the system has cleaned
            // up completely (ReplicatorCount == 0 and removed from static dict).
            var instances = new GameObject[3];
            var ids = new ulong[3];
            for (int i = 0; i < 3; i++)
            {
                instances[i] = SpawnObject(_statePrefab, m_ServerNetworkManager);
                ids[i] = instances[i].GetComponent<NetworkObject>().NetworkObjectId;
            }

            for (int i = 0; i < 3; i++)
                yield return WaitForSpawnOnAllClients(ids[i]);

            Assert.IsTrue(
                EntityReplicationSystem.TryGet(m_ServerNetworkManager, out var serverSystem),
                "System must exist after spawning entities.");
            Assert.AreEqual(3, serverSystem.ReplicatorCount,
                "System must track all 3 spawned replicators.");

            for (int i = 0; i < 3; i++)
                instances[i].GetComponent<NetworkObject>().Despawn(destroy: true);

            // Give NGO a couple of frames to process despawns.
            for (int i = 0; i < 5; i++) yield return s_DefaultWaitForTick;

            // After all replicators unregistered, the system auto-disposes and
            // removes itself from the static dictionary.
            Assert.IsFalse(
                EntityReplicationSystem.TryGet(m_ServerNetworkManager, out _),
                "System must self-dispose and remove from static registry when the last replicator unregisters.");
        }
    }
}
