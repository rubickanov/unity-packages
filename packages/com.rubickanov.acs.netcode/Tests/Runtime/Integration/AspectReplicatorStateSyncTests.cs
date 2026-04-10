using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Runtime.Netcode.Tests.Integration
{
    /// <summary>
    /// Integration coverage for the server-auth state replication path:
    /// server writes a ReactiveProperty → OnServerTick packs dirty fields →
    /// BroadcastStateRpc → every non-authority peer applies the new value.
    /// Also covers the initial-sync snapshot path used by late-joining clients
    /// (regression #1).
    /// </summary>
    public class AspectReplicatorStateSyncTests : AspectReplicatorIntegrationTestBase
    {
        [UnityTest]
        public IEnumerator ServerWritesField_AllClientsApplyAfterTick()
        {
            // Baseline server-auth propagation — if this fails, the entire
            // replication pipeline is broken and the remaining state tests
            // are meaningless noise.
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
            AssertOnTimeout("Server-auth write did not propagate to all clients within the default tick window.");
        }

        [UnityTest]
        public IEnumerator ServerWritesMultipleFieldsSameTick_SinglePayloadAppliedAtomically()
        {
            // Two dirty fields in one frame must land on the clients as a
            // single RPC and both values must match. If the packing step
            // miscounts bytes for either field (regression class covered by
            // the unit mixed-type round-trip test), the second field would
            // decode to garbage here.
            //
            // The test uses the state prefab's two-field aspect — bit 0 is
            // OwnerValue and bit 1 is ServerValue (sorted by name). Writing
            // ServerValue from the server is fine; OwnerValue is owner-auth,
            // and on the host the server IS the owner, so the server-side
            // write on a host-owned entity also makes bit 0 dirty and goes
            // out in the same tick.
            var serverInstance = SpawnObject(_statePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            var serverAspect = GetStateAspectOnClient(m_ServerNetworkManager, networkObjectId);
            serverAspect.ServerValue.Value = 7;
            serverAspect.OwnerValue.Value = 3.14f;

            yield return WaitForConditionOrTimeOut(() =>
            {
                for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
                {
                    var aspect = GetStateAspectOnClient(m_ClientNetworkManagers[i], networkObjectId);
                    if (aspect.ServerValue.Value != 7) return false;
                    if (!Mathf.Approximately(aspect.OwnerValue.Value, 3.14f)) return false;
                }
                return true;
            });
            AssertOnTimeout("Multi-field write did not converge on all clients.");
        }

        [UnityTest]
        public IEnumerator LateJoiningClient_ReceivesFullStateSnapshot_RegressionOne()
        {
            // Regression #1: late joiners must receive current state for
            // every replicated field, not just fields that went dirty after
            // they connected. The initial-sync path is RequestInitialStateRpc
            // on client spawn → server builds a full-mask snapshot → client
            // applies.
            //
            // Scenario: spawn entity, write while only 2 pure clients are
            // observing, wait for convergence, then connect a 3rd client.
            // The new client must see the current value, not default(int).
            var serverInstance = SpawnObject(_statePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            GetStateAspectOnClient(m_ServerNetworkManager, networkObjectId).ServerValue.Value = 1234;

            yield return WaitForConditionOrTimeOut(() =>
            {
                for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
                {
                    if (GetStateAspectOnClient(m_ClientNetworkManagers[i], networkObjectId).ServerValue.Value != 1234)
                        return false;
                }
                return true;
            });
            AssertOnTimeout("Pre-existing clients never saw the initial server write.");

            // Bring up a new client after the server already wrote.
            yield return CreateAndStartNewClient();
            var lateJoiner = m_ClientNetworkManagers[m_ClientNetworkManagers.Length - 1];

            // Wait for the late joiner to spawn the entity AND to apply its
            // initial-sync snapshot. The spawn event itself lands first; the
            // initial-sync RPC reply arrives a tick later.
            yield return WaitForConditionOrTimeOut(() =>
            {
                if (!lateJoiner.SpawnManager.SpawnedObjects.ContainsKey(networkObjectId)) return false;
                return GetStateAspectOnClient(lateJoiner, networkObjectId).ServerValue.Value == 1234;
            });
            AssertOnTimeout("Late-joining client did not receive the full-state snapshot.");
        }

        [UnityTest]
        public IEnumerator LateJoiningClient_ReceivesNeverDirtyFieldsToo_RegressionOne()
        {
            // Reinforcement of #1: the initial-sync path builds a full mask
            // regardless of each binding's IsDirty flag, so fields that the
            // server wrote *before* any tick fired (effectively default(T)
            // for a field that was never explicitly assigned) must still
            // arrive. Here the server never touches OwnerValue at all — it
            // should land on the late joiner as the current server-side value
            // (0f, the constructor default), proving the path doesn't filter
            // by dirty state.
            var serverInstance = SpawnObject(_statePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            // Write only ServerValue, deliberately leaving OwnerValue untouched.
            GetStateAspectOnClient(m_ServerNetworkManager, networkObjectId).ServerValue.Value = 99;

            // Give the existing clients a chance to converge first.
            yield return WaitForConditionOrTimeOut(() =>
            {
                for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
                {
                    if (GetStateAspectOnClient(m_ClientNetworkManagers[i], networkObjectId).ServerValue.Value != 99)
                        return false;
                }
                return true;
            });
            AssertOnTimeout("Pre-existing clients never saw the server write.");

            yield return CreateAndStartNewClient();
            var lateJoiner = m_ClientNetworkManagers[m_ClientNetworkManagers.Length - 1];

            yield return WaitForConditionOrTimeOut(() =>
            {
                if (!lateJoiner.SpawnManager.SpawnedObjects.ContainsKey(networkObjectId)) return false;
                var aspect = GetStateAspectOnClient(lateJoiner, networkObjectId);
                // ServerValue must arrive; OwnerValue must still be at its
                // constructor default 0f (the server never wrote it, and the
                // late joiner did not start with anything else).
                return aspect.ServerValue.Value == 99
                       && Mathf.Approximately(aspect.OwnerValue.Value, 0f);
            });
            AssertOnTimeout("Late-joining client's initial-sync did not include both fields.");
        }

        [UnityTest]
        public IEnumerator ServerStopsWriting_BindingGoesClean_NoFurtherStateDrift()
        {
            // Proxy check for ClearDirty: after a write converges, the
            // server-side binding's IsDirty flag must flip back to false,
            // OnServerTick must stop pushing the same value every tick, and
            // client values must stay put.
            //
            // Without ClearDirty, the server would re-broadcast the same
            // field on every tick — observable as steady-state RPC spam.
            // We can't count RPCs from test code, but two indirect proofs
            // suffice: (a) the binding's IsDirty is false after a quiet
            // window, and (b) client values do not drift.
            var serverInstance = SpawnObject(_statePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            GetStateAspectOnClient(m_ServerNetworkManager, networkObjectId).ServerValue.Value = 55;

            yield return WaitForConditionOrTimeOut(() =>
            {
                for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
                {
                    if (GetStateAspectOnClient(m_ClientNetworkManagers[i], networkObjectId).ServerValue.Value != 55)
                        return false;
                }
                return true;
            });
            AssertOnTimeout("Write did not converge.");

            // Let several ticks pass without any further writes.
            for (int i = 0; i < 5; i++) yield return s_DefaultWaitForTick;

            // (a) server-side binding must be clean.
            var serverReplicator = GetReplicatorOnClient(m_ServerNetworkManager, networkObjectId);
            var bindingsField = typeof(AspectReplicator).GetField("_bindings",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var serverBindings = (ReplicatedFieldBinding[])bindingsField!.GetValue(serverReplicator)!;
            for (int i = 0; i < serverBindings.Length; i++)
            {
                Assert.IsFalse(serverBindings[i].IsDirty,
                    $"Binding {i} on server still dirty after the write converged — ClearDirty did not fire.");
            }

            // (b) clients still hold the post-write value.
            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
            {
                Assert.AreEqual(55,
                    GetStateAspectOnClient(m_ClientNetworkManagers[i], networkObjectId).ServerValue.Value,
                    $"Client {m_ClientNetworkManagers[i].LocalClientId} drifted away from the post-write value.");
            }
        }
    }
}
