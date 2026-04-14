using NUnit.Framework;
using Rubickanov.ACS.Runtime.Netcode;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class OwnerSubmitTickSyncTests
    {
        // OwnerSubmitTickSync is the extracted seam from EntityReplicator.ApplyOwnerSubmission
        // (which itself needs a live NetworkManager). The math is independent of both, so
        // we exercise the struct directly — no GameObject, no NetworkObject, no reflection.

        [Test]
        public void Update_FirstSample_SeedsExactly()
        {
            var sync = new OwnerSubmitTickSync();

            sync.Update(serverTick: 1000, senderTick: 950);

            Assert.AreEqual(50.0, sync.Offset, 1e-9, "first sample must seed offset exactly to (server - sender)");
            Assert.IsTrue(sync.HasOffset, "flag must be set after the first sample");
        }

        [Test]
        public void Update_SecondSample_BlendsWithEmaAlpha10Percent()
        {
            var sync = new OwnerSubmitTickSync();

            sync.Update(serverTick: 1000, senderTick: 950); // seed = 50
            sync.Update(serverTick: 2000, senderTick: 1900); // raw = 100

            // 0.9 * 50 + 0.1 * 100 = 55
            Assert.AreEqual(55.0, sync.Offset, 1e-9);
        }

        [Test]
        public void Update_LongDriftSequence_ConvergesTowardNewDelta()
        {
            var sync = new OwnerSubmitTickSync();

            // Seed at delta = 50, then drift to delta = 80 over 200 samples.
            // EMA with alpha 0.1 reaches within ~1% of the new value in ~50 samples.
            sync.Update(1000, 950);
            for (int i = 0; i < 200; i++)
                sync.Update(2000 + i, 1920 + i); // delta = 80 every step

            Assert.AreEqual(80.0, sync.Offset, 0.01, "offset must converge to the new sustained delta");
        }

        [Test]
        public void Update_AfterReset_NextSampleReseeds()
        {
            var sync = new OwnerSubmitTickSync();

            sync.Update(1000, 950); // seed = 50
            sync.Reset();

            // After reset, the next sample must seed exactly — not blend with the stale value.
            sync.Update(serverTick: 5000, senderTick: 4700);

            Assert.AreEqual(300.0, sync.Offset, 1e-9, "post-reset sample must re-seed exactly");
            Assert.IsTrue(sync.HasOffset);
        }

        [Test]
        public void Reset_ClearsOffsetAndFlag()
        {
            var sync = new OwnerSubmitTickSync();
            sync.Update(1000, 950);

            sync.Reset();

            Assert.AreEqual(0.0, sync.Offset);
            Assert.IsFalse(sync.HasOffset);
        }

        [Test]
        public void Update_SingleJitterSpike_DoesNotMoveOffsetVisibly()
        {
            var sync = new OwnerSubmitTickSync();

            // Steady state offset = 50 across many samples, then one outlier.
            sync.Update(1000, 950);
            for (int i = 0; i < 100; i++)
                sync.Update(2000 + i, 1950 + i); // delta = 50

            double before = sync.Offset;
            sync.Update(3000, 2900); // outlier delta = 100
            double after = sync.Offset;

            // alpha 0.1 means a single outlier shifts offset by exactly 0.1 * (raw - prev).
            // 50 ticks of jitter must not produce more than ~5 ticks of movement.
            Assert.AreEqual(before + 0.1 * (100 - before), after, 1e-9);
            Assert.LessOrEqual(System.Math.Abs(after - before), 5.0,
                "single 50-tick jitter spike must move EMA offset by < 5 ticks");
        }
    }
}
