using System;
using System.Collections;
using NUnit.Framework;
using R3;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Runtime.Netcode.Tests.Integration
{
    /// <summary>
    /// Integration coverage for replicated event dispatch: server-auth events
    /// broadcast to every non-host peer, owner-auth events round-trip via
    /// SubmitOwnerEventRpc → server relay, and neither path may double-fire on
    /// the emitting peer. The host-side IsHost guard in <c>DispatchEvent</c>
    /// and the owner-side IsOwner guard together keep every peer's receive
    /// count at exactly one per emit.
    /// </summary>
    public class AspectReplicatorEventTests : AspectReplicatorIntegrationTestBase
    {
        [UnityTest]
        public IEnumerator HostFiresServerAuthEvent_AllPeersReceiveExactlyOnce()
        {
            // Host is authority for a server-auth event. Calling OnNext on the
            // Subject fires all subscribers locally (host test sub + the
            // replicator's OnLocalEvent which sends BroadcastEventRpc). The
            // RPC is SendTo.NotServer, so the host does NOT re-enter its own
            // DispatchEvent — host count stays at 1. Both pure clients receive
            // via DispatchEvent → ApplyFromNetwork → Subject.OnNext, so each
            // lands at exactly 1.
            var serverInstance = SpawnObject(_eventPrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            var counts = new int[m_NetworkManagers.Length];
            var disposables = new IDisposable[m_NetworkManagers.Length];
            for (int i = 0; i < m_NetworkManagers.Length; i++)
            {
                int capture = i;
                disposables[i] = GetEventAspectOnClient(m_NetworkManagers[i], networkObjectId)
                    .ServerEvent.Subscribe(_ => counts[capture]++);
            }

            try
            {
                GetEventAspectOnClient(m_ServerNetworkManager, networkObjectId).ServerEvent.OnNext(7);

                // Host fires synchronously, clients need a network round-trip.
                yield return WaitForConditionOrTimeOut(() =>
                {
                    for (int i = 0; i < counts.Length; i++)
                        if (counts[i] < 1) return false;
                    return true;
                });
                AssertOnTimeout("Server-auth event did not reach every peer.");

                // Spin a few extra ticks so any stray self-relay would have
                // landed, then assert exactly one per peer. A count of 2 on
                // the host would mean the IsHost guard in DispatchEvent is
                // gone — the same bug that makes every event fire twice on
                // the authority side.
                for (int i = 0; i < 5; i++) yield return s_DefaultWaitForTick;

                for (int i = 0; i < counts.Length; i++)
                {
                    Assert.AreEqual(1, counts[i],
                        $"Peer {m_NetworkManagers[i].LocalClientId} received {counts[i]} events, expected exactly 1.");
                }
            }
            finally
            {
                for (int i = 0; i < disposables.Length; i++) disposables[i]?.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator PureClientOwnerFiresOwnerAuthEvent_RelaysToServerAndOtherClient()
        {
            // Full owner-auth event pipeline: client 0 is owner, emits
            // OwnerEvent → SubmitOwnerEventRpc → server's HandleOwnerEvent
            // relays via BroadcastEventRpc to every non-server peer AND fires
            // its own Subject via ApplyFromNetwork. Server and client 1 must
            // both see the value exactly once.
            var serverInstance = SpawnObject(_eventPrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            serverInstance.GetComponent<NetworkObject>().ChangeOwnership(m_ClientNetworkManagers[0].LocalClientId);
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

            int serverCount = 0, client1Count = 0;
            var serverSub = GetEventAspectOnClient(m_ServerNetworkManager, networkObjectId)
                .OwnerEvent.Subscribe(_ => serverCount++);
            var client1Sub = GetEventAspectOnClient(m_ClientNetworkManagers[1], networkObjectId)
                .OwnerEvent.Subscribe(_ => client1Count++);

            try
            {
                GetEventAspectOnClient(m_ClientNetworkManagers[0], networkObjectId).OwnerEvent.OnNext(42);

                yield return WaitForConditionOrTimeOut(() => serverCount >= 1 && client1Count >= 1);
                AssertOnTimeout("Owner-auth event did not relay from owner to server and other client.");

                // Let any stray relay echoes flush before the exact-count assert.
                for (int i = 0; i < 5; i++) yield return s_DefaultWaitForTick;

                Assert.AreEqual(1, serverCount,
                    "Server must dispatch an owner-submitted event exactly once through ApplyFromNetwork.");
                Assert.AreEqual(1, client1Count,
                    "Other pure client must dispatch the owner-relayed event exactly once.");
            }
            finally
            {
                serverSub.Dispose();
                client1Sub.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator PureClientOwnerFiresOwnerAuthEvent_OwnerDoesNotDoubleReceive()
        {
            // The IsOwner guard inside DispatchEvent is the only thing keeping
            // the owner's Subject from firing twice: once for the local emit,
            // and again when the server's relay BroadcastEventRpc comes back
            // to the owner. Before the guard, owner-auth events would always
            // double on the emitting owner.
            //
            // This test exists specifically so a refactor that rewires the
            // relay path has a single red line to trip on.
            var serverInstance = SpawnObject(_eventPrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            serverInstance.GetComponent<NetworkObject>().ChangeOwnership(m_ClientNetworkManagers[0].LocalClientId);
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

            int ownerCount = 0;
            var ownerSub = GetEventAspectOnClient(m_ClientNetworkManagers[0], networkObjectId)
                .OwnerEvent.Subscribe(_ => ownerCount++);

            // Sentinel sub on client 1 — used only to know the relay round-
            // trip has actually completed before we assert the owner's count.
            // Without this the test could pass vacuously by finishing before
            // the relay echo would have arrived.
            int client1Count = 0;
            var client1Sub = GetEventAspectOnClient(m_ClientNetworkManagers[1], networkObjectId)
                .OwnerEvent.Subscribe(_ => client1Count++);

            try
            {
                GetEventAspectOnClient(m_ClientNetworkManagers[0], networkObjectId).OwnerEvent.OnNext(99);

                // Wait for the echo to reach client 1 — by this point any
                // echo that would have reached the owner has also arrived.
                yield return WaitForConditionOrTimeOut(() => client1Count >= 1);
                AssertOnTimeout("Relay did not reach the sentinel client — cannot reason about owner echo.");

                // Extra ticks to flush anything in flight.
                for (int i = 0; i < 5; i++) yield return s_DefaultWaitForTick;

                Assert.AreEqual(1, ownerCount,
                    "Owner received the relay echo of its own emit — IsOwner guard in DispatchEvent is missing.");
            }
            finally
            {
                ownerSub.Dispose();
                client1Sub.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator HostIsOwner_OwnerAuthEventBroadcastsDirectly()
        {
            // When the host owns the entity, the owner-auth broadcaster lookup
            // in OnNetworkSpawn picks the regular BroadcastEventRpc rather
            // than SubmitOwnerEventRpc — because useOwnerSubmit is gated on
            // !IsServer. The result should look indistinguishable from the
            // server-auth case: host fires locally once, both clients receive
            // exactly once via the direct broadcast. This test guards the
            // authority-branch selection; a future refactor that forgets the
            // !IsServer gate would route through the owner-submit path and
            // make the host's SubmitOwnerEventRpc trigger an extra RPC.
            var serverInstance = SpawnObject(_eventPrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            // Host (m_ServerNetworkManager) is the owner by default — confirm.
            Assert.AreEqual(m_ServerNetworkManager.LocalClientId,
                m_ServerNetworkManager.SpawnManager.SpawnedObjects[networkObjectId].OwnerClientId,
                "Precondition: host must own the entity by default on SpawnObject(server).");

            var counts = new int[m_NetworkManagers.Length];
            var disposables = new IDisposable[m_NetworkManagers.Length];
            for (int i = 0; i < m_NetworkManagers.Length; i++)
            {
                int capture = i;
                disposables[i] = GetEventAspectOnClient(m_NetworkManagers[i], networkObjectId)
                    .OwnerEvent.Subscribe(_ => counts[capture]++);
            }

            try
            {
                GetEventAspectOnClient(m_ServerNetworkManager, networkObjectId).OwnerEvent.OnNext(123);

                yield return WaitForConditionOrTimeOut(() =>
                {
                    for (int i = 0; i < counts.Length; i++)
                        if (counts[i] < 1) return false;
                    return true;
                });
                AssertOnTimeout("Host-owner OwnerEvent did not reach every peer.");

                for (int i = 0; i < 5; i++) yield return s_DefaultWaitForTick;

                for (int i = 0; i < counts.Length; i++)
                {
                    Assert.AreEqual(1, counts[i],
                        $"Peer {m_NetworkManagers[i].LocalClientId} received {counts[i]} events, expected exactly 1.");
                }
            }
            finally
            {
                for (int i = 0; i < disposables.Length; i++) disposables[i]?.Dispose();
            }
        }
    }
}
