using System.Reflection;
using NUnit.Framework;
using Rubickanov.ACS.Runtime.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class OwnerSubmitTickOffsetEmaTests
    {
        // ---- Fixture ------------------------------------------------------------
        //
        // UpdateOwnerSubmitTickOffset is the extracted seam from ApplyOwnerSubmission
        // (which itself needs a live NetworkManager). The math is independent of both,
        // so a bare AspectReplicator on a GameObject is enough — we read back the
        // private offset/flag fields via reflection.

        private GameObject _go;
        private AspectReplicator _replicator;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject(nameof(OwnerSubmitTickOffsetEmaTests));
            _go.AddComponent<NetworkObject>();
            _replicator = _go.AddComponent<AspectReplicator>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        private static double GetOffset(AspectReplicator r)
        {
            var f = typeof(AspectReplicator).GetField("_ownerSubmitTickOffset", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "AspectReplicator must have a private field '_ownerSubmitTickOffset' — rename detected?");
            return (double)f.GetValue(r);
        }

        private static bool GetHasOffset(AspectReplicator r)
        {
            var f = typeof(AspectReplicator).GetField("_hasOwnerSubmitTickOffset", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "AspectReplicator must have a private field '_hasOwnerSubmitTickOffset' — rename detected?");
            return (bool)f.GetValue(r);
        }

        // ---- Tests --------------------------------------------------------------

        [Test]
        public void UpdateOwnerSubmitTickOffset_FirstSample_SeedsExactly()
        {
            _replicator.UpdateOwnerSubmitTickOffset(serverTick: 1000, senderTick: 950);

            Assert.AreEqual(50.0, GetOffset(_replicator), 1e-9, "first sample must seed offset exactly to (server - sender)");
            Assert.IsTrue(GetHasOffset(_replicator), "flag must be set after the first sample");
        }

        [Test]
        public void UpdateOwnerSubmitTickOffset_SecondSample_BlendsWithEmaAlpha10Percent()
        {
            _replicator.UpdateOwnerSubmitTickOffset(serverTick: 1000, senderTick: 950); // seed = 50
            _replicator.UpdateOwnerSubmitTickOffset(serverTick: 2000, senderTick: 1900); // raw = 100

            // 0.9 * 50 + 0.1 * 100 = 55
            Assert.AreEqual(55.0, GetOffset(_replicator), 1e-9);
        }

        [Test]
        public void UpdateOwnerSubmitTickOffset_LongDriftSequence_ConvergesTowardNewDelta()
        {
            // Seed at delta = 50, then drift to delta = 80 over 200 samples.
            // EMA with alpha 0.1 reaches within ~1% of the new value in ~50 samples.
            _replicator.UpdateOwnerSubmitTickOffset(1000, 950);
            for (int i = 0; i < 200; i++)
                _replicator.UpdateOwnerSubmitTickOffset(2000 + i, 1920 + i); // delta = 80 every step

            Assert.AreEqual(80.0, GetOffset(_replicator), 0.01, "offset must converge to the new sustained delta");
        }

        [Test]
        public void UpdateOwnerSubmitTickOffset_AfterFlagReset_NextSampleReseeds()
        {
            // Simulate the OnGainedOwnership / OnNetworkDespawn reset paths.
            _replicator.UpdateOwnerSubmitTickOffset(1000, 950); // seed = 50

            typeof(AspectReplicator)
                .GetField("_hasOwnerSubmitTickOffset", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_replicator, false);

            // After reset, the next sample must seed exactly — not blend with the stale value.
            _replicator.UpdateOwnerSubmitTickOffset(serverTick: 5000, senderTick: 4700);

            Assert.AreEqual(300.0, GetOffset(_replicator), 1e-9, "post-reset sample must re-seed exactly");
            Assert.IsTrue(GetHasOffset(_replicator));
        }

        [Test]
        public void UpdateOwnerSubmitTickOffset_SingleJitterSpike_DoesNotMoveOffsetVisibly()
        {
            // Steady state offset = 50 across many samples, then one outlier.
            _replicator.UpdateOwnerSubmitTickOffset(1000, 950);
            for (int i = 0; i < 100; i++)
                _replicator.UpdateOwnerSubmitTickOffset(2000 + i, 1950 + i); // delta = 50

            double before = GetOffset(_replicator);
            _replicator.UpdateOwnerSubmitTickOffset(3000, 2900); // outlier delta = 100
            double after = GetOffset(_replicator);

            // alpha 0.1 means a single outlier shifts offset by exactly 0.1 * (raw - prev).
            // 50 ticks of jitter must not produce more than ~5 ticks of movement.
            Assert.AreEqual(before + 0.1 * (100 - before), after, 1e-9);
            Assert.LessOrEqual(System.Math.Abs(after - before), 5.0,
                "single 50-tick jitter spike must move EMA offset by < 5 ticks");
        }
    }
}
