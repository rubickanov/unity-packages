using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Runtime.Netcode.Tests.Integration
{
    /// <summary>
    /// Integration coverage for owner-auth replication: pure-client owner
    /// writes -> ACS_OwnerSubmit to server -> server relays via
    /// ACS_StateBatch -> other clients apply; plus host-as-owner direct
    /// broadcast, and the #19 initial-sync race around
    /// <c>OwnerWroteSinceSpawn</c>.
    /// </summary>
    public class EntityReplicatorOwnerAuthTests : EntityReplicatorIntegrationTestBase
    {
        [UnityTest]
        public IEnumerator PureClientOwnerWritesOwnerAuthField_RelaysToOtherClients()
        {
            // The full owner-auth pipeline: client 0 gains ownership, writes
            // OwnerValue, server receives ACS_OwnerSubmit, relays via
            // ACS_StateBatch, and client 1 ends up with the new value.
            // This is the most load-bearing path — if it fails, nothing built
            // on owner-auth replication works at all.
            var serverInstance = SpawnObject(_statePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            // Hand ownership to client 0. NGO requires the ownership change
            // to happen on the server side.
            var serverNetworkObject = m_ServerNetworkManager.SpawnManager.SpawnedObjects[networkObjectId];
            serverNetworkObject.ChangeOwnership(m_ClientNetworkManagers[0].LocalClientId);

            // Wait for ownership to propagate to all peers.
            yield return WaitForConditionOrTimeOut(() =>
            {
                for (int i = 0; i < m_NetworkManagers.Length; i++)
                {
                    if (m_NetworkManagers[i].SpawnManager.SpawnedObjects[networkObjectId].OwnerClientId
                        != m_ClientNetworkManagers[0].LocalClientId)
                        return false;
                }
                return true;
            });
            AssertOnTimeout("Ownership change did not propagate.");

            // Client 0 is now the authority for OwnerValue. Write from there.
            GetStateAspectOnClient(m_ClientNetworkManagers[0], networkObjectId).OwnerValue.Value = 1.5f;

            // Server and client 1 must both converge on 1.5f.
            yield return WaitForConditionOrTimeOut(() =>
            {
                if (!Mathf.Approximately(
                    GetStateAspectOnClient(m_ServerNetworkManager, networkObjectId).OwnerValue.Value, 1.5f))
                    return false;
                if (!Mathf.Approximately(
                    GetStateAspectOnClient(m_ClientNetworkManagers[1], networkObjectId).OwnerValue.Value, 1.5f))
                    return false;
                return true;
            });
            AssertOnTimeout("Owner-auth write from pure client did not relay to server or other clients.");
        }

        [UnityTest]
        public IEnumerator OwnerWritesServerAuthField_ServerLogsWarning_NotRelayed()
        {
            // Defense-in-depth: if a pure-client owner tries to write a
            // server-auth field (e.g. via a bug in game code), the server's
            // ApplyOwnerSubmission handler must reject it with a warning and
            // NOT relay the bogus value. Without this guard, any client
            // could silently forge server-auth state.
            //
            // We exercise this by forging an ACS_OwnerSubmit payload on the
            // server side that includes the server-auth field's dirty bit and
            // feeding it directly to the server's replicator.
            var serverInstance = SpawnObject(_statePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            serverInstance.GetComponent<NetworkObject>().ChangeOwnership(m_ClientNetworkManagers[0].LocalClientId);
            yield return WaitForConditionOrTimeOut(() =>
            {
                for (int i = 0; i < m_NetworkManagers.Length; i++)
                {
                    if (m_NetworkManagers[i].SpawnManager.SpawnedObjects[networkObjectId].OwnerClientId
                        != m_ClientNetworkManagers[0].LocalClientId) return false;
                }
                return true;
            });
            AssertOnTimeout("Ownership change did not propagate.");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                @"\[EntityReplicator\] Owner submitted server-auth field index .* on '.*'\. Dropping\."));

            // Build a forged owner-submit payload that includes the server-auth
            // field. Field order is alphabetical: [0] = OwnerValue (float),
            // [1] = ServerValue (int). With 2 bindings, maskByteCount = 1.
            // We set both bits in the mask — the server must accept [0] (owner-auth)
            // but reject [1] (server-auth) with a warning.
            var serverReplicator = GetReplicatorOnClient(m_ServerNetworkManager, networkObjectId);
            ApplyForgedOwnerSubmission(serverReplicator, ownerValue: 0f, serverValue: 666);

            // Spin a few ticks so any post-warning relay would propagate.
            // ServerValue on client 1 must stay at the spawn default (0).
            for (int i = 0; i < 5; i++) yield return s_DefaultWaitForTick;

            Assert.AreEqual(0,
                GetStateAspectOnClient(m_ClientNetworkManagers[1], networkObjectId).ServerValue.Value,
                "Client 1's ServerValue must not be corrupted by the owner's forged write.");
        }

        [UnityTest]
        public IEnumerator HostIsOwner_OwnerAuthFieldBroadcastsDirectly()
        {
            // When the host owns the entity, the centralized system's server
            // tick picks up both server-auth and owner-auth dirty fields in
            // one pass (IsOwner && IsServer -> no ACS_OwnerSubmit). The
            // outcome must be a single batch broadcast that lands on both
            // pure clients.
            var serverInstance = SpawnObject(_statePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            // Host (m_ServerNetworkManager) is owner by default when spawning
            // without explicit ownership transfer. Confirm.
            var hostOwnerId = m_ServerNetworkManager.SpawnManager
                .SpawnedObjects[networkObjectId].OwnerClientId;
            Assert.AreEqual(m_ServerNetworkManager.LocalClientId, hostOwnerId,
                "Precondition: host must own the entity by default on SpawnObject(server).");

            GetStateAspectOnClient(m_ServerNetworkManager, networkObjectId).OwnerValue.Value = 2.25f;

            yield return WaitForConditionOrTimeOut(() =>
            {
                for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
                {
                    if (!Mathf.Approximately(
                        GetStateAspectOnClient(m_ClientNetworkManagers[i], networkObjectId).OwnerValue.Value, 2.25f))
                        return false;
                }
                return true;
            });
            AssertOnTimeout("Host-owned OwnerValue did not reach both pure clients.");
        }

        [UnityTest]
        public IEnumerator OwnerWroteLocally_ThenInitialSyncArrives_LocalValuePreserved_RegressionNineteen()
        {
            // Regression #19: the pure-client owner writes its owner-auth
            // field locally BEFORE the initial-sync reply arrives from the
            // server. The initial-sync payload must not clobber the fresh
            // local value. The guard is
            // OwnerWroteSinceSpawn → StateApplyMode.SkipOwnerAuthIfLocallyWritten.
            //
            // The natural race (spawn → ChangeOwnership → client writes →
            // SendInitialStateRpc arrives) has a loopback window too small to
            // hit reliably in editor tests. We simulate the ordering
            // deterministically: spawn, transfer ownership, force
            // OwnerWroteSinceSpawn=true on the owner's binding via reflection
            // (mirroring what a real local write would have done), then
            // invoke the replicator's SendInitialStateRpc handler directly on
            // the owner with a forged server snapshot. If the guard holds, the
            // owner's local value is preserved.
            var serverInstance = SpawnObject(_statePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            serverInstance.GetComponent<NetworkObject>().ChangeOwnership(m_ClientNetworkManagers[0].LocalClientId);
            yield return WaitForConditionOrTimeOut(() =>
            {
                for (int i = 0; i < m_NetworkManagers.Length; i++)
                {
                    if (m_NetworkManagers[i].SpawnManager.SpawnedObjects[networkObjectId].OwnerClientId
                        != m_ClientNetworkManagers[0].LocalClientId) return false;
                }
                return true;
            });
            AssertOnTimeout("Ownership change did not propagate.");

            // Write locally on the owner and wait until the binding's
            // OwnerWroteSinceSpawn has flipped (R3 subscribe replay will also
            // set it synchronously, but we reset it on OnGainedOwnership, so
            // the only way to observe a true flip is through a real write).
            GetStateAspectOnClient(m_ClientNetworkManagers[0], networkObjectId).OwnerValue.Value = 42f;

            var ownerReplicator = GetReplicatorOnClient(m_ClientNetworkManagers[0], networkObjectId);
            var bindingsField = typeof(EntityReplicator).GetField("_bindings",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var ownerBindings = (ReplicatedFieldBinding[])bindingsField.GetValue(ownerReplicator)!;
            // [0] = OwnerValue (alphabetical sort).
            Assert.IsTrue(ownerBindings[0].OwnerWroteSinceSpawn,
                "Precondition: a real local write must flip OwnerWroteSinceSpawn.");

            // Forge a server snapshot payload where OwnerValue is 999f — the
            // stale value the server would have sent in the real race. The
            // wire format is: int serverTick, ulong fullMask, then raw field
            // bytes in binding-index order.
            var payload = BuildInitialSyncPayload(ownerReplicator, ownerServerValue: 0, ownerOwnerValue: 999f);

            // Invoke ApplyStateBuffer directly in the SkipOwnerAuthIfLocallyWritten
            // mode that SendInitialStateRpc uses. The field is internal and
            // reachable via InternalsVisibleTo.
            ownerReplicator.ApplyStateBuffer(payload, StateApplyMode.SkipOwnerAuthIfLocallyWritten);

            Assert.AreEqual(42f,
                GetStateAspectOnClient(m_ClientNetworkManagers[0], networkObjectId).OwnerValue.Value,
                "Owner's local OwnerValue must survive a stale initial-sync snapshot.");

            yield break;
        }

        // ---- Helpers --------------------------------------------------------

        private static unsafe byte[] BuildInitialSyncPayload(
            EntityReplicator replicator, int ownerServerValue, float ownerOwnerValue)
        {
            // Mirrors BuildInitialSyncPayload's writer layout: int serverTick,
            // byte[maskLen] fullMask, then every binding's bytes in order. Field
            // order is alphabetical on name, so [0] = OwnerValue (float) and
            // [1] = ServerValue (int) for the state test aspect. With 2 bindings,
            // maskLen = 1.
            var writer = new FastBufferWriter(256, Allocator.Temp);
            try
            {
                writer.WriteValueSafe((int)0); // serverTick — value doesn't matter for this test
                byte fullMask = 0b11; // full mask over the two bindings (1 byte)
                writer.WriteBytesSafe(&fullMask, 1);
                var owner = ownerOwnerValue;
                writer.WriteBytesSafe((byte*)&owner, sizeof(float));
                var server = ownerServerValue;
                writer.WriteBytesSafe((byte*)&server, sizeof(int));
                return writer.ToArray();
            }
            finally
            {
                writer.Dispose();
            }
        }

        /// <summary>
        /// Builds a forged ACS_OwnerSubmit payload with both owner-auth and
        /// server-auth fields dirty, then feeds it to the server's replicator.
        /// Extracted from the iterator to satisfy C#'s "no unsafe in iterators" rule.
        /// </summary>
        private static unsafe void ApplyForgedOwnerSubmission(
            EntityReplicator serverReplicator, float ownerValue, int serverValue)
        {
            var forgedWriter = new FastBufferWriter(64, Allocator.Temp);
            try
            {
                byte mask = 0b11; // both fields dirty
                forgedWriter.WriteBytesSafe(&mask, 1);
                var ov = ownerValue;
                forgedWriter.WriteBytesSafe((byte*)&ov, sizeof(float));
                var sv = serverValue;
                forgedWriter.WriteBytesSafe((byte*)&sv, sizeof(int));

                var forgedReader = new FastBufferReader(forgedWriter, Allocator.Temp);
                try
                {
                    serverReplicator.ApplyOwnerSubmission(forgedReader, senderTick: 0);
                }
                finally
                {
                    forgedReader.Dispose();
                }
            }
            finally
            {
                forgedWriter.Dispose();
            }
        }
    }
}
