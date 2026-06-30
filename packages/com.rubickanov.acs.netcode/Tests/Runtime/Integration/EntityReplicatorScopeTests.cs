using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Runtime.Netcode.Tests.Integration
{
    /// <summary>
    /// Integration coverage for <c>EntityReplicator.ApplyNetworkScopes</c>:
    /// components marked <see cref="NetworkScope.ServerOnly"/> or
    /// <see cref="NetworkScope.OwnerOnly"/> must be disabled on peers that
    /// don't match the scope, and the ownership-transfer path must re-evaluate
    /// owner-only components. Also covers regression #3 (scope does not
    /// cascade past a nested NetworkObject) and #16 (a scope-disabled
    /// component must not subscribe on OnNetworkSpawn).
    /// </summary>
    public class EntityReplicatorScopeTests : EntityReplicatorIntegrationTestBase
    {
        [UnityTest]
        public IEnumerator ServerOnlyComponent_OnPureClient_DisabledAfterSpawn()
        {
            // The scope prefab carries a [ServerOnly] marker. After spawn:
            // server (host) keeps it enabled, pure clients must have it
            // disabled by ApplyNetworkScopes.
            var serverInstance = SpawnObject(_scopePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            var serverMarker = m_ServerNetworkManager.SpawnManager
                .SpawnedObjects[networkObjectId].GetComponent<ServerOnlyMarkerComponent>();
            Assert.IsTrue(serverMarker.enabled,
                "ServerOnly marker must be enabled on the server/host peer.");

            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
            {
                var clientMarker = m_ClientNetworkManagers[i].SpawnManager
                    .SpawnedObjects[networkObjectId].GetComponent<ServerOnlyMarkerComponent>();
                Assert.IsFalse(clientMarker.enabled,
                    $"Client {m_ClientNetworkManagers[i].LocalClientId}'s ServerOnly marker must be disabled by ApplyNetworkScopes.");
            }
        }

        [UnityTest]
        public IEnumerator OwnerOnlyComponent_TracksOwnershipTransfer()
        {
            // OwnerOnly must flip enabled on ownership transitions, not just
            // at spawn time. Start with host ownership → both pure clients
            // have it disabled, server has it enabled. ChangeOwnership to
            // client 0 → client 0 gains, server and client 1 lose.
            var serverInstance = SpawnObject(_scopePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            OwnerOnlyMarkerComponent MarkerOn(NetworkManager nm) =>
                nm.SpawnManager.SpawnedObjects[networkObjectId].GetComponent<OwnerOnlyMarkerComponent>();

            // Step 1: host is owner by default → host's marker enabled,
            // both pure clients disabled.
            Assert.IsTrue(MarkerOn(m_ServerNetworkManager).enabled,
                "Host (initial owner) must have OwnerOnly marker enabled.");
            Assert.IsFalse(MarkerOn(m_ClientNetworkManagers[0]).enabled,
                "Non-owner pure client 0 must have OwnerOnly marker disabled.");
            Assert.IsFalse(MarkerOn(m_ClientNetworkManagers[1]).enabled,
                "Non-owner pure client 1 must have OwnerOnly marker disabled.");

            // Step 2: transfer ownership to client 0.
            serverInstance.GetComponent<NetworkObject>().ChangeOwnership(
                m_ClientNetworkManagers[0].LocalClientId);

            // Wait for ownership + ReapplyOwnerScope effects to land on all peers.
            yield return WaitForConditionOrTimeOut(() =>
            {
                if (MarkerOn(m_ClientNetworkManagers[0]).enabled == false) return false;
                if (MarkerOn(m_ServerNetworkManager).enabled == true) return false;
                if (MarkerOn(m_ClientNetworkManagers[1]).enabled == true) return false;
                return true;
            });
            AssertOnTimeout("OwnerOnly marker did not track ChangeOwnership → client 0.");
        }

        [UnityTest]
        public IEnumerator NestedNetworkObject_ScopeDoesNotCascadeFromParent_RegressionThree()
        {
            // Regression #3: ApplyNetworkScopes uses GetComponentsInChildren,
            // which naively would walk into nested NetworkObjects and scope-
            // disable their components too. The fix filters by
            // GetComponentInParent<NetworkObject>() == myNetworkObject —
            // children that belong to an inner NO are skipped.
            //
            // This test spawns the nested-scope prefab, which has a
            // ServerOnly marker on BOTH the parent (governed by the parent
            // replicator) and the child (which belongs to a different NO).
            // On a pure client, the parent marker must be disabled (correct
            // scope behavior) but the child marker must remain untouched by
            // the parent's ApplyNetworkScopes — its own NetworkObject has no
            // replicator in this fixture, so it stays at whatever its default
            // enabled state is.
            //
            // The assertion is: child.enabled is UNCHANGED by the parent's
            // scope pass. With the bug, the child would be disabled on pure
            // clients because the parent walked into it.
            //
            // NGO does not support spawning runtime-instantiated prefabs with
            // nested NetworkObjects — it logs an error we must acknowledge.
            // NGO emits a Warning ("[Netcode] Spawning NetworkObjects with nested
            // NetworkObjects is only supported for scene objects...") when a runtime-
            // instantiated prefab carries nested NetworkObjects. We deliberately do NOT
            // LogAssert.Expect it: it's incidental NGO noise (a Warning cannot fail the
            // test as an unexpected log), and its level + "[Netcode]" prefix have drifted
            // across NGO versions — pinning it just makes the test brittle.
            var serverInstance = SpawnObject(_nestedScopePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            // The child's NetworkObject was added as a child in the prefab.
            // After spawn it has its own NetworkObjectId on the server — but
            // because the fixture child NO is not itself a registered prefab,
            // it may not get spawned on clients at all. For this test we only
            // need the server-side instance, because the cascade bug would
            // affect the parent's OnNetworkSpawn on every peer via
            // GetComponentsInChildren before the child's spawn replication.
            var serverParent = m_ServerNetworkManager.SpawnManager
                .SpawnedObjects[networkObjectId].gameObject;
            var serverParentMarker = serverParent.GetComponent<ServerOnlyMarkerComponent>();
            Assert.IsTrue(serverParentMarker.enabled,
                "Server-side parent ServerOnly marker must be enabled on the host.");

            // On a pure client, find the parent instance — the child marker
            // lives on a nested GameObject that parent's GetComponentsInChildren
            // would visit. The nested NO filter in ApplyNetworkScopes must
            // prevent the parent replicator from toggling the child's marker.
            //
            // We verify by reflection over the transform hierarchy rather
            // than SpawnedObjects lookup, because the child NO has no
            // registered prefab entry on the client and may not appear in
            // the client's SpawnedObjects at all — the parent's spawn still
            // produces the child GameObject via Instantiate(prefab) on that
            // client (the child was a transform child of the parent prefab
            // GameObject at test-prefab construction time).
            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
            {
                var clientParent = m_ClientNetworkManagers[i].SpawnManager
                    .SpawnedObjects[networkObjectId].gameObject;
                var clientParentMarker = clientParent.GetComponent<ServerOnlyMarkerComponent>();
                Assert.IsFalse(clientParentMarker.enabled,
                    $"Client {m_ClientNetworkManagers[i].LocalClientId}'s parent marker must be scope-disabled.");

                // The critical regression assertion: the child's marker must
                // NOT have been disabled by the parent replicator's pass. If
                // the child transform exists at all on this client, its
                // ServerOnlyMarkerComponent should still be in its natural
                // default state (enabled=true) because no replicator on the
                // parent's NO was allowed to touch it.
                var childTransform = clientParent.transform.Find("NestedScopeChild");
                if (childTransform == null) continue; // some NGO paths skip nested un-registered NOs
                var childMarker = childTransform.GetComponent<ServerOnlyMarkerComponent>();
                if (childMarker == null) continue;
                Assert.IsTrue(childMarker.enabled,
                    $"Client {m_ClientNetworkManagers[i].LocalClientId}: child NO's marker must not be touched by parent's ApplyNetworkScopes — regression #3.");
            }
        }

        [UnityTest]
        public IEnumerator NestedNetworkObject_ScopeMarkedInsideNestedNO_LogsWarning()
        {
            // The nested prefab puts a ServerOnlyMarkerComponent on the child NO. The parent's
            // NetworkScopeController walks the hierarchy and MUST NOT apply scope to components
            // under a nested NetworkObject — but if it stays silent, a user mistakenly attaching
            // [NetworkScope] to a nested-NO component will never learn the attribute was ignored.
            // The controller logs one warning per such component per peer. Spawning on host +
            // two pure clients drives ApplyInitial three times, so three warnings are expected.
            // NGO emits a Warning ("[Netcode] Spawning NetworkObjects with nested
            // NetworkObjects is only supported for scene objects...") when a runtime-
            // instantiated prefab carries nested NetworkObjects. We deliberately do NOT
            // LogAssert.Expect it: it's incidental NGO noise (a Warning cannot fail the
            // test as an unexpected log), and its level + "[Netcode]" prefix have drifted
            // across NGO versions — pinning it just makes the test brittle.
            var warningRegex = new System.Text.RegularExpressions.Regex(
                @"\[NetworkScopeController\] ServerOnlyMarkerComponent on '.*' is marked \[NetworkScope\(ServerOnly\)\] but sits under a nested NetworkObject");
            for (int i = 0; i < m_NetworkManagers.Length; i++)
                LogAssert.Expect(LogType.Warning, warningRegex);

            var serverInstance = SpawnObject(_nestedScopePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);
        }

        [UnityTest]
        public IEnumerator ServerOnlyComponent_OnPureClient_NeverSubscribes_RegressionSixteen()
        {
            // Regression #16: a scope-disabled EntityNetworkComponent must
            // never fire OnSubscribe on pure clients. The spawn sequence is:
            // OnNetworkSpawn on EntityReplicator → ApplyNetworkScopes flips
            // enabled=false synchronously → OnNetworkSpawn on the marker →
            // TrySubscribe sees enabled==false and bails. Before the fix,
            // TrySubscribe was tied only to the spawn callback and ignored
            // the enabled flag, so ServerOnly markers would subscribe on
            // pure clients and their R3 reactions would fire regardless of
            // Behaviour.enabled.
            //
            // The edit-mode lifecycle test covers this at the unit level;
            // here we prove the same invariant holds through a real NGO
            // spawn on a pure client peer.
            var serverInstance = SpawnObject(_scopePrefab, m_ServerNetworkManager);
            var networkObjectId = serverInstance.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForSpawnOnAllClients(networkObjectId);

            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
            {
                var marker = m_ClientNetworkManagers[i].SpawnManager
                    .SpawnedObjects[networkObjectId].GetComponent<ServerOnlyMarkerComponent>();
                Assert.AreEqual(0, marker.SubscribeCount,
                    $"Client {m_ClientNetworkManagers[i].LocalClientId}'s ServerOnly marker subscribed on spawn despite being scope-disabled — regression #16.");
            }

            // Sanity: the host side DID subscribe (enabled=true there), so
            // the counter is wired correctly and the pure-client zero above
            // is not a wiring bug.
            var hostMarker = m_ServerNetworkManager.SpawnManager
                .SpawnedObjects[networkObjectId].GetComponent<ServerOnlyMarkerComponent>();
            Assert.AreEqual(1, hostMarker.SubscribeCount,
                "Server/host ServerOnly marker must subscribe normally.");
        }
    }
}
