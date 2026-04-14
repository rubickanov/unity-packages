using System.Collections;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Runtime.Netcode.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for steps 6 and 7 of the prediction pipeline:
    /// pure-client owner gathers input → <c>ACS_Input:&lt;TInput&gt;</c> named
    /// message → server-side <see cref="ISimulate{TInput}"/> writes to a
    /// <c>[Replicated(Predicted = true)]</c> field → existing replication path
    /// delivers the result to a pure observer client → owner reconciles the
    /// arriving authoritative state against its snapshot buffer and replays
    /// locally-buffered inputs so the owner's local view never snaps backwards.
    /// </summary>
    public class PredictionPipelineTests : EntityReplicatorIntegrationTestBase
    {
        protected GameObject _predictionPrefab = null!;

        protected override void OnServerAndClientsCreated()
        {
            base.OnServerAndClientsCreated();

            _predictionPrefab = CreateNetworkObjectPrefab("PredictionEntity");
            _predictionPrefab.AddComponent<MonoEntity>();
            _predictionPrefab.AddComponent<EntityReplicator>();
            _predictionPrefab.AddComponent<PredictionTestAspectRegistrar>();
            _predictionPrefab.AddComponent<TestInputProvider>();
            _predictionPrefab.AddComponent<TestMovementSimulator>();
        }

        [UnityTest]
        public IEnumerator OwnerClientInputDrivesServerSimulate_PositionReplicatesToObserver()
        {
            // Full step 6 pipeline smoke test. The host spawns an entity and
            // hands ownership to client 0. Client 0's TestInputProvider returns
            // a constant Move = (1, 0) every tick. PredictionManager on the
            // client packs it into ACS_Input and sends to the server; the
            // server-side simulator applies it to the replicated Position. The
            // replication system's existing broadcast delivers the new Position
            // to client 1 (pure observer).
            var serverInstance = SpawnObject(_predictionPrefab, m_ServerNetworkManager);
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

            // Arm the owner-side provider. Before this call Gather() returns
            // default(TestInputCommand), so no motion is produced.
            var ownerProvider = GetInputProviderOnClient(m_ClientNetworkManagers[0], networkObjectId);
            ownerProvider.Move = new Vector2(1f, 0f);

            // Server Simulate runs on NetworkTickSystem.Tick — over ~120 ticks
            // (4 s at 30 Hz) Position.x must grow well past zero on the pure
            // observer (client 1). We assert a generous lower bound rather
            // than an exact value because tick cadence is not frame-perfect
            // in editor tests.
            yield return WaitForConditionOrTimeOut(() =>
                GetPredictionAspectOnClient(m_ClientNetworkManagers[1], networkObjectId).Position.Value.x > 0.5f);
            AssertOnTimeout("Observer client never saw Position drift — owner-input → server-Simulate → replicate pipe is broken.");

            // Cross-check the server drove it, not just local prediction on
            // the owner — the observer's value originates from the server
            // broadcast.
            var serverX = GetPredictionAspectOnClient(m_ServerNetworkManager, networkObjectId).Position.Value.x;
            Assert.Greater(serverX, 0.5f,
                "Server Position.x did not advance — ISimulate was never driven on the authority side.");
        }

        [UnityTest]
        public IEnumerator OwnerReconcilesAgainstServerState_OwnerPositionXDoesNotSnapBack()
        {
            // Step 7 contract: under a constant forward input, the owner's
            // local Position.x must be monotonic non-decreasing. Before the
            // snapshot-buffer + reconcile path landed, each ACS_StateBatch
            // broadcast overwrote the owner's prediction with the ~RTT-old
            // authoritative value, producing a visible snap-back. With
            // reconcile, ApplyStateBuffer → replay(serverTick+1..currentTick)
            // keeps the owner's view ahead of authority, never behind.
            var serverInstance = SpawnObject(_predictionPrefab, m_ServerNetworkManager);
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

            var ownerProvider = GetInputProviderOnClient(m_ClientNetworkManagers[0], networkObjectId);
            ownerProvider.Move = new Vector2(1f, 0f);

            var ownerAspect = GetPredictionAspectOnClient(m_ClientNetworkManagers[0], networkObjectId);

            // Sample over ~2 s. Allow a tiny negative epsilon for floating-point
            // jitter inside a single Simulate step — the reconcile path
            // overwrites the predicted field then immediately re-runs the same
            // inputs, so the net delta per reconcile is ~0 at worst, never the
            // >= tickDelta regression a true snap-back would produce. A
            // snap-back under Move.x = 1 at 30 Hz produces a step of ~0.033
            // per reconcile, so 1e-4 tolerance is well below any real snap.
            const int sampleCount = 60;
            const float tolerance = 1e-4f;

            float previous = ownerAspect.Position.Value.x;
            float maxRegression = 0f;
            int regressionSamples = 0;

            for (int i = 0; i < sampleCount; i++)
            {
                yield return s_DefaultWaitForTick;

                float current = ownerAspect.Position.Value.x;
                float delta = current - previous;
                if (delta < -tolerance)
                {
                    regressionSamples++;
                    if (-delta > maxRegression) maxRegression = -delta;
                }
                previous = current;
            }

            Assert.AreEqual(0, regressionSamples,
                $"Owner Position.x regressed on {regressionSamples}/{sampleCount} samples " +
                $"(max backward delta {maxRegression:F4}). Reconcile did not replay buffered " +
                $"inputs on top of the authoritative state — expected monotonic growth under a constant input.");

            // Sanity check: the owner actually moved forward overall. If the
            // simulator stopped running we'd hit 0 regressions trivially.
            Assert.Greater(ownerAspect.Position.Value.x, 0.5f,
                "Owner Position.x never advanced — the Simulate pass or input provider is not wired up.");
        }

        // ---- Helpers --------------------------------------------------------

        private static PredictionTestAspect GetPredictionAspectOnClient(NetworkManager client, ulong networkObjectId)
        {
            var go = client.SpawnManager.SpawnedObjects[networkObjectId].gameObject;
            return go.GetComponent<MonoEntity>().Require<PredictionTestAspect>();
        }

        private static TestInputProvider GetInputProviderOnClient(NetworkManager client, ulong networkObjectId)
        {
            var go = client.SpawnManager.SpawnedObjects[networkObjectId].gameObject;
            return go.GetComponent<TestInputProvider>();
        }
    }

    // ---- Test payload + aspect ---------------------------------------------

    /// <summary>
    /// Minimal <see cref="IInputCommand"/> carrying a 2D move vector. Must be
    /// unmanaged — the prediction pipeline uses unsafe byte copies for wire
    /// serialization.
    /// </summary>
    public struct TestInputCommand : IInputCommand
    {
        public Vector2 Move;
    }

    /// <summary>
    /// Aspect whose <c>Position</c> is both replicated and marked for prediction.
    /// <c>Predicted = true</c> drives the step-7 snapshot/reconcile path: the owner
    /// captures a post-Simulate snapshot each tick and, when the authoritative
    /// Position arrives via the replication broadcast, replays buffered inputs
    /// on top of it to avoid a visible snap-back.
    /// </summary>
    public sealed class PredictionTestAspect : IEntityAspect
    {
        [Replicated(Predicted = true)]
        public ReactiveProperty<Vector3> Position = new(Vector3.zero);
    }

    // ---- Aspect registrar --------------------------------------------------

    public sealed class PredictionTestAspectRegistrar : MonoBehaviour, IEntityComponent
    {
        public PredictionTestAspect Aspect = default!;

        private void Awake()
        {
            var context = GetComponentInParent<MonoEntity>();
            Aspect = context.Require<PredictionTestAspect>();
        }
    }

    // ---- Input provider ----------------------------------------------------

    /// <summary>
    /// Returns whatever <see cref="Move"/> is set to at the moment
    /// <see cref="PredictionManager{TInput}"/> pulls input on each tick. Tests
    /// mutate <c>Move</c> directly rather than simulating Input System events.
    /// </summary>
    public sealed class TestInputProvider : MonoBehaviour, IInputProvider<TestInputCommand>
    {
        public Vector2 Move;

        public TestInputCommand Gather() => new() { Move = Move };
    }

    // ---- Simulator ---------------------------------------------------------

    /// <summary>
    /// Integrates <see cref="TestInputCommand.Move"/> into
    /// <see cref="PredictionTestAspect.Position"/> once per tick on both the
    /// owner client (local prediction) and the server (authority).
    /// </summary>
    public sealed class TestMovementSimulator : MonoBehaviour, ISimulate<TestInputCommand>, IEntityComponent
    {
        private PredictionTestAspect _aspect = null!;

        private void Awake()
        {
            var context = GetComponentInParent<MonoEntity>();
            _aspect = context.Require<PredictionTestAspect>();
        }

        public void Simulate(in TestInputCommand input, float dt)
        {
            var move3 = new Vector3(input.Move.x, 0f, input.Move.y);
            _aspect.Position.Value += move3 * dt;
        }
    }
}
