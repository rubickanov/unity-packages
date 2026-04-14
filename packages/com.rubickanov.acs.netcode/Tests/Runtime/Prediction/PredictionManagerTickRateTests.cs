using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class PredictionManagerTickRateTests
    {
        // If NetworkTickSystem.TickRate is 0, the manager's
        // _tickDelta collapses to 0 and every Simulate(in input, 0f) call becomes a no-op
        // — motion freezes silently with no warning, no crash, and no test failure until
        // someone plays the game. GetOrCreate must refuse to build the manager so the
        // misconfiguration surfaces as "prediction never registered" instead of "inputs
        // vanish silently".

        private struct TestInput : IInputCommand
        {
            public int Value;
        }

        private static readonly FieldInfo s_SystemsField =
            typeof(PredictionManager<TestInput>).GetField("s_Systems",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new System.InvalidOperationException(
                "PredictionManager<TInput>.s_Systems field renamed?");

        private static readonly FieldInfo s_WarnedZeroTickRateField =
            typeof(PredictionManager<TestInput>).GetField("s_warnedZeroTickRate",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new System.InvalidOperationException(
                "PredictionManager<TInput>.s_warnedZeroTickRate field renamed?");

        // NetworkManager.NetworkTickSystem has a private setter — we assemble a
        // stand-in NetworkManager for these tests without starting NGO, so we
        // inject the tick system via reflection through its setter.
        private static readonly MethodInfo s_NetworkTickSystemSetter =
            typeof(NetworkManager).GetProperty("NetworkTickSystem",
                BindingFlags.Instance | BindingFlags.Public)!
            .GetSetMethod(nonPublic: true)
            ?? throw new System.InvalidOperationException(
                "NetworkManager.NetworkTickSystem has no accessible setter — NGO internals renamed?");

        // NetworkTickSystem's ctor rejects tickRate == 0 (NGO's own invariant).
        // To force the misconfig state we build the tick system with a placeholder
        // value and then lower its TickRate through the internal setter.
        private static readonly MethodInfo s_TickRateSetter =
            typeof(NetworkTickSystem).GetProperty("TickRate",
                BindingFlags.Instance | BindingFlags.Public)!
            .GetSetMethod(nonPublic: true)
            ?? throw new System.InvalidOperationException(
                "NetworkTickSystem.TickRate has no accessible setter — NGO internals renamed?");

        private readonly List<GameObject> _toDestroy = new();

        [SetUp]
        public void ClearStaticCaches()
        {
            // Both static caches must be reset between tests — carryover would
            // either return a cached manager (masking the refuse path) or
            // silently swallow the LogError (s_warnedZeroTickRate=true).
            ((IDictionary<NetworkManager, PredictionManager<TestInput>>)s_SystemsField.GetValue(null)!).Clear();
            s_WarnedZeroTickRateField.SetValue(null, false);
        }

        [TearDown]
        public void DestroySpawnedManagers()
        {
            foreach (var go in _toDestroy)
                if (go != null) Object.DestroyImmediate(go);
            _toDestroy.Clear();
        }

        [Test]
        public void GetOrCreate_WithTickRateZero_ReturnsNullAndLogsError()
        {
            var nm = CreateStandaloneNetworkManager(tickRate: 0);

            var result = PredictionManager<TestInput>.GetOrCreate(nm);

            Assert.IsNull(result,
                "GetOrCreate must refuse to build the manager when tick rate is 0 — a silent build would yield _tickDelta=0 and freeze prediction.");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                @"\[PredictionManager\] NetworkTickSystem\.TickRate is 0"));
        }

        [Test]
        public void GetOrCreate_WithTickRateZero_SecondCallDoesNotRelogError()
        {
            // The warn-once guard prevents one-log-per-predicted-entity spam when
            // the user spawns many entities against a misconfigured NetworkManager.
            // If this test fails, the fix is likely a flipped s_warnedZeroTickRate
            // default or a reset somewhere on the hot path.
            var nm = CreateStandaloneNetworkManager(tickRate: 0);

            // First call logs the error. Swallow it here so the second-call
            // assertion is not polluted by the unexpected-log check that
            // Unity test runner applies at teardown.
            _ = PredictionManager<TestInput>.GetOrCreate(nm);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                @"\[PredictionManager\] NetworkTickSystem\.TickRate is 0"));

            var result = PredictionManager<TestInput>.GetOrCreate(nm);

            Assert.IsNull(result, "second call must also refuse — not cache a null, re-enter the guard path");
            // No LogAssert.Expect for this call — if the guard is broken and a
            // second error leaks out, LogAssert.NoUnexpectedReceived in teardown
            // will fail the test.
        }

        // The positive-tick-rate happy path is covered end-to-end by every
        // integration test that spawns a predicted entity (PredictionPipelineTests
        // etc.) — we don't add a duplicate pure-unit check here because a proper
        // positive-rate GetOrCreate requires a live NetworkManager with
        // CustomMessagingManager, which means StartHost, which means the full
        // integration fixture.

        // ---- Helpers ------------------------------------------------------------

        private NetworkManager CreateStandaloneNetworkManager(uint tickRate)
        {
            // Minimal stand-in for the zero-rate refuse path only: a
            // GameObject-hosted NetworkManager with a manually injected
            // NetworkTickSystem. NGO is NOT started — GetOrCreate bails on the
            // zero-rate check before it would read CustomMessagingManager, so
            // we don't need a real transport or handshake. This keeps the
            // test synchronous.
            Assert.AreEqual(0u, tickRate,
                "helper supports the zero-rate refuse path only; the happy path requires a full NGO fixture");

            var go = new GameObject($"TestNetworkManager-tr{tickRate}");
            _toDestroy.Add(go);
            var nm = go.AddComponent<NetworkManager>();

            // NGO's NetworkTickSystem ctor rejects 0 directly, so construct with a
            // placeholder and then lower the rate via the internal setter. The
            // point of this fixture is to put the manager into the misconfigured
            // state the refuse path is meant to detect.
            var tickSystem = new NetworkTickSystem(1, 0, 0);
            s_TickRateSetter.Invoke(tickSystem, new object[] { tickRate });
            s_NetworkTickSystemSetter.Invoke(nm, new object[] { tickSystem });

            return nm;
        }
    }
}
