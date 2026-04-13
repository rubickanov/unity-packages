using System;
using System.Collections.Generic;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime.Netcode;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class AuthorityRenderBindingTests
    {
        // ---- Fake clock --------------------------------------------------------
        //
        // AuthorityRenderBinding<T>.Clock is a static Func<double> per closed generic type.
        // Each T used in the fixture needs its own override. Tests route wall-clock reads
        // through _fakeNow so sample times and TickRender computation are deterministic.
        //
        // Timing model: sample logic is driven through ApplyFromNetwork / PushSample in
        // most tests, which routes directly into RecordSample(value) using _fakeNow as the
        // sample timestamp. SubscribeAsAuthority/SubscribeForLocalSampling pathways are
        // tested in dedicated cases because R3's Subscribe replays the current value
        // synchronously (bootstrap sample) and `Value = x; Value = x` is suppressed, both
        // of which would complicate generic two-sample tests.
        //
        // Sample-gap ranges that matter (see AuthorityRenderBinding constants):
        //   < 10ms   → coalesce into _curr (intra-frame reconcile writes).
        //   10..66ms → slide pair (normal tick-to-tick motion @ 30–100 Hz).
        //   > 66ms   → stale bootstrap (idle gap; drop _prev, hold _curr).
        // Tests that want slide behavior use a 30ms step (mid of range).

        private const double TickStep = 0.030;

        private double _fakeNow;
        private readonly List<Action> _clockRestorers = new();
        private readonly List<ReplicatedFieldBinding> _bindings = new();

        [SetUp]
        public void SetUp()
        {
            _fakeNow = 0.0;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var binding in _bindings)
                binding.OnDespawn();
            _bindings.Clear();

            foreach (var restore in _clockRestorers)
                restore();
            _clockRestorers.Clear();
        }

        private (AuthorityRenderBinding<T> binding, ReactiveProperty<T> reactive)
            CreateBinding<T>(T initial = default) where T : unmanaged
        {
            var original = AuthorityRenderBinding<T>.Clock;
            AuthorityRenderBinding<T>.Clock = () => _fakeNow;
            _clockRestorers.Add(() => AuthorityRenderBinding<T>.Clock = original);

            var reactive = new ReactiveProperty<T>(initial);
            // tickDelta = 1/30 (30 Hz) keeps coalesce window ≈ 10 ms and stale threshold
            // ≈ 83 ms — slightly different from the old hard-coded constants (10 ms /
            // 66 ms) but wide enough that every existing test's sample gap still
            // classifies into the same branch (<10 ms coalesce, 30 ms slide, 500 ms
            // stale bootstrap). See ISSUES.md #23.
            var binding = (AuthorityRenderBinding<T>)
                ReplicatedFieldBindingFactory.Create(reactive, typeof(T), FieldBindingKind.AuthorityRendered, 1.0 / 30);
            _bindings.Add(binding);
            return (binding, reactive);
        }

        // Pushes a sample via the network path. AuthorityRenderBinding.ApplyFromNetwork
        // records the value using Clock() — i.e. _fakeNow — as the sample time, so callers
        // control sample timing by setting _fakeNow before invoking.
        private static unsafe void PushSample<T>(AuthorityRenderBinding<T> binding, T value)
            where T : unmanaged
        {
            var writer = new FastBufferWriter(sizeof(T), Allocator.Temp);
            try
            {
                writer.WriteBytesSafe((byte*)&value, sizeof(T));
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    binding.ReadFrom(reader);
                    binding.ApplyFromNetwork(0.0); // receivedTime ignored by this binding
                }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }
        }

        // ---- Empty / bootstrap -------------------------------------------------

        [Test]
        public void TickRender_NoSamples_LeavesInterpolatedValueDefault()
        {
            var (binding, _) = CreateBinding<Vector3>();

            _fakeNow = 10.0;
            binding.TickRender(0.0);

            Assert.AreEqual(default(Vector3), binding.InterpolatedValue);
        }

        [Test]
        public void SingleSample_BootstrapsInterpolatedValue_WithoutTickRender()
        {
            var (binding, _) = CreateBinding<Vector3>();

            _fakeNow = 1.0;
            PushSample(binding, new Vector3(3f, 4f, 5f));

            // First sample short-circuits: _curr and _interpolatedValue are both set immediately.
            Assert.AreEqual(new Vector3(3f, 4f, 5f), binding.InterpolatedValue);
        }

        [Test]
        public void SingleSample_TickRender_HoldsThatValue()
        {
            var (binding, _) = CreateBinding<Vector3>();

            _fakeNow = 1.0;
            PushSample(binding, new Vector3(7f, 8f, 9f));

            // Advance clock far beyond sample; with only one sample there's no pair to lerp,
            // so TickRender must hold _curr instead of producing default or garbage.
            _fakeNow = 5.0;
            binding.TickRender(0.0);

            Assert.AreEqual(new Vector3(7f, 8f, 9f), binding.InterpolatedValue);
        }

        // ---- Lerp between samples (slide window: 30ms gap) --------------------

        [Test]
        public void TwoSamples_NowAtCurrTime_AlphaZero_RendersPrev()
        {
            var (binding, _) = CreateBinding<float>();

            _fakeNow = 1.000;
            PushSample(binding, 0f);
            _fakeNow = 1.030;
            PushSample(binding, 10f);

            // now == _currTime → (1.030 - 1.030) / 0.030 = 0 → lerp(prev, curr, 0) = prev.
            binding.TickRender(0.0);

            Assert.AreEqual(0f, binding.InterpolatedValue, 1e-5f);
        }

        [Test]
        public void TwoSamples_NowAtMidpoint_RendersMidpoint()
        {
            var (binding, _) = CreateBinding<float>();

            _fakeNow = 1.000;
            PushSample(binding, 0f);
            _fakeNow = 1.030;
            PushSample(binding, 10f);

            // span = 0.030, now = 1.045 → (1.045 - 1.030) / 0.030 = 0.5 → lerp(0,10,0.5) = 5.
            _fakeNow = 1.045;
            binding.TickRender(0.0);

            Assert.AreEqual(5f, binding.InterpolatedValue, 1e-5f);
        }

        [Test]
        public void TwoSamples_NowBeyondOneSpan_ClampsAtCurr()
        {
            var (binding, _) = CreateBinding<float>();

            _fakeNow = 1.000;
            PushSample(binding, 0f);
            _fakeNow = 1.030;
            PushSample(binding, 10f);

            // No extrapolation: clock stalled far past curr → raw >> 1 → clamp to curr.
            _fakeNow = 99.0;
            binding.TickRender(0.0);

            Assert.AreEqual(10f, binding.InterpolatedValue, 1e-5f);
        }

        [Test]
        public void TwoSamples_NowBeforeCurrTime_ClampsAtPrev()
        {
            var (binding, _) = CreateBinding<float>();

            _fakeNow = 1.000;
            PushSample(binding, 0f);
            _fakeNow = 1.030;
            PushSample(binding, 10f);

            // now < currTime can only happen if TickRender runs before wall-clock advances past
            // the write — raw < 0 → clamp to 0 → prev.
            _fakeNow = 1.015;
            binding.TickRender(0.0);

            Assert.AreEqual(0f, binding.InterpolatedValue, 1e-5f);
        }

        // ---- Sliding pair ------------------------------------------------------

        [Test]
        public void ThirdSample_SlidesPair_PrevBecomesSecondSample()
        {
            var (binding, _) = CreateBinding<float>();

            _fakeNow = 1.000;
            PushSample(binding, 100f);
            _fakeNow = 1.030;
            PushSample(binding, 200f);
            _fakeNow = 1.060;
            PushSample(binding, 300f);  // pair is now (_prev=200@1.030, _curr=300@1.060).

            _fakeNow = 1.075;
            binding.TickRender(0.0);

            // Midpoint between 200 and 300 = 250. If stale pair (100,200) was used we'd get 150.
            Assert.AreEqual(250f, binding.InterpolatedValue, 1e-5f);
        }

        [Test]
        public void ZeroSpan_TwoSamplesSameInstant_HoldsCurr()
        {
            var (binding, _) = CreateBinding<float>();

            _fakeNow = 1.0;
            PushSample(binding, 5f);
            PushSample(binding, 7f);   // second write at identical _fakeNow → coalesce into _curr.

            _fakeNow = 1.5;
            binding.TickRender(0.0);

            // Coalesce kept _hasPrev=false → TickRender holds _curr=7f.
            Assert.AreEqual(7f, binding.InterpolatedValue, 1e-5f);
        }

        // ---- Vector3 / Quaternion lerpers --------------------------------------

        [Test]
        public void TwoSamples_Vector3_LerpsAtMidpoint()
        {
            var (binding, _) = CreateBinding<Vector3>();

            _fakeNow = 0.000;
            PushSample(binding, Vector3.zero);
            _fakeNow = 0.030;
            PushSample(binding, new Vector3(10f, 20f, 30f));

            _fakeNow = 0.045;
            binding.TickRender(0.0);

            Assert.AreEqual(5f, binding.InterpolatedValue.x, 1e-5f);
            Assert.AreEqual(10f, binding.InterpolatedValue.y, 1e-5f);
            Assert.AreEqual(15f, binding.InterpolatedValue.z, 1e-5f);
        }

        [Test]
        public void TwoSamples_Quaternion_MidpointPreservesUnitLength()
        {
            var (binding, _) = CreateBinding(Quaternion.identity);

            _fakeNow = 0.000;
            PushSample(binding, Quaternion.identity);
            _fakeNow = 0.030;
            PushSample(binding, Quaternion.Euler(0f, 90f, 0f));

            _fakeNow = 0.045;
            binding.TickRender(0.0);

            var q = binding.InterpolatedValue;
            float length = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            Assert.AreEqual(1f, length, 1e-4f);
        }

        // ---- Subscribe pathways ------------------------------------------------

        [Test]
        public void SubscribeAsAuthority_ReplaysInitialValue_BootstrapsInterpolatedValue()
        {
            // R3 Subscribe fires synchronously with the current value; that's the mechanism
            // that records the spawn-time sample so the very first frame has something to show.
            _fakeNow = 0.0;
            var (binding, _) = CreateBinding<float>(initial: 42f);

            var bag = new DisposableBag();
            binding.SubscribeAsAuthority(ref bag);

            Assert.AreEqual(42f, binding.InterpolatedValue, 1e-5f);
            // Replay counts as an authority-side write: dirty + ownerWroteSinceSpawn both set.
            Assert.IsTrue(binding.IsDirty);
            Assert.IsTrue(binding.OwnerWroteSinceSpawn);
        }

        [Test]
        public void SubscribeAsAuthority_WriteAfterReplay_RecordsSecondSample_AndMarksDirty()
        {
            _fakeNow = 0.000;
            var (binding, reactive) = CreateBinding<float>(initial: 0f);
            var bag = new DisposableBag();
            binding.SubscribeAsAuthority(ref bag);
            binding.ClearDirty();

            _fakeNow = 0.030;
            reactive.Value = 10f;   // replay gave sample(0, 0.000); this is sample(10, 0.030).

            _fakeNow = 0.045;
            binding.TickRender(0.0);

            // Midpoint between sample(0, 0.000) and sample(10, 0.030) at now=0.045.
            Assert.AreEqual(5f, binding.InterpolatedValue, 1e-5f);
            Assert.IsTrue(binding.IsDirty, "Authority write must mark the binding dirty for relay.");
        }

        [Test]
        public void SubscribeForLocalSampling_RecordsSample_WithoutMarkingDirty()
        {
            _fakeNow = 0.000;
            var (binding, reactive) = CreateBinding<float>(initial: 0f);

            var bag = new DisposableBag();
            binding.SubscribeForLocalSampling(ref bag);
            // Replay at _fakeNow=0 gave sample(0, 0.000); subscribing must NOT mark dirty.
            Assert.IsFalse(binding.IsDirty, "Local-sampling subscribe must not mark dirty on replay.");

            _fakeNow = 0.030;
            reactive.Value = 10f;

            _fakeNow = 0.045;
            binding.TickRender(0.0);

            // Smoothing still works...
            Assert.AreEqual(5f, binding.InterpolatedValue, 1e-5f);
            // ...but dirty flag stays clear — predicted-owner must not trigger owner-auth relay.
            Assert.IsFalse(binding.IsDirty, "Predicted-owner local sampling must not mark the field dirty.");
        }

        // ---- Factory & flags ---------------------------------------------------

        [Test]
        public void Factory_AuthorityRenderedKind_ProducesAuthorityRenderBinding()
        {
            var (binding, _) = CreateBinding<float>();

            Assert.IsInstanceOf<AuthorityRenderBinding<float>>(binding);
        }

        [Test]
        public void IsInterpolated_AuthorityRendered_True()
        {
            var (binding, _) = CreateBinding<float>();
            Assert.IsTrue(binding.IsInterpolated);
        }

        [Test]
        public void Factory_UnsupportedType_FallsBackToPlainBinding()
        {
            // int has no registered Lerp<int>; AuthorityRendered kind should degrade to Plain.
            var reactive = new ReactiveProperty<int>(42);

            var binding = ReplicatedFieldBindingFactory.Create(
                reactive, typeof(int), FieldBindingKind.AuthorityRendered, 1.0 / 30);

            Assert.IsInstanceOf<ReplicatedFieldBinding<int>>(binding);
            Assert.IsNotInstanceOf<AuthorityRenderBinding<int>>(binding);
            Assert.IsFalse(binding.IsInterpolated);
        }

        // ---- .Smooth() integration ---------------------------------------------

        [Test]
        public void Smooth_ReturnsInterpolatedValue_NotRawValue()
        {
            var (binding, reactive) = CreateBinding<float>();

            _fakeNow = 1.000;
            PushSample(binding, 0f);
            _fakeNow = 1.030;
            PushSample(binding, 10f);

            _fakeNow = 1.045;
            binding.TickRender(0.0);

            Assert.AreEqual(5f, reactive.Smooth(), 1e-5f);
            // Raw value is the latest sample (10f), not the smoothed midpoint.
            Assert.AreEqual(10f, reactive.Value, 1e-5f);
        }

        [Test]
        public void OnDespawn_UnregistersFromInterpolationRegistry_SmoothFallsBackToValue()
        {
            var (binding, reactive) = CreateBinding<float>();

            _fakeNow = 1.000;
            PushSample(binding, 0f);
            _fakeNow = 1.030;
            PushSample(binding, 10f);
            _fakeNow = 1.045;
            binding.TickRender(0.0);
            Assert.AreEqual(5f, reactive.Smooth(), 1e-5f);

            binding.OnDespawn();
            _bindings.Remove(binding); // avoid double-OnDespawn from TearDown

            // Post-despawn Smooth() must return .Value (10f), not the cached 5f.
            Assert.AreEqual(10f, reactive.Smooth(), 1e-5f);
        }

        // ---- ApplyFromNetwork paths --------------------------------------------

        [Test]
        public void ApplyFromNetwork_WritesRawValueToReactive_ForOwnershipTransferCase()
        {
            // After ownership transfer, the former owner starts receiving network snapshots
            // via ApplyFromNetwork. No subscribe-sampler is attached, so ApplyFromNetwork is
            // the only sampling path — it must both write .Value and feed the render pair.
            var (binding, reactive) = CreateBinding<float>();

            _fakeNow = 1.000;
            PushSample(binding, 100f);
            Assert.AreEqual(100f, reactive.Value, 1e-5f);

            _fakeNow = 1.030;
            PushSample(binding, 200f);
            Assert.AreEqual(200f, reactive.Value, 1e-5f);

            _fakeNow = 1.045;
            binding.TickRender(0.0);
            Assert.AreEqual(150f, binding.InterpolatedValue, 1e-5f);
        }

        [Test]
        public void ApplyFromNetwork_AfterSubscribeForLocalSampling_DoesNotSample_PreventsDoubleSample()
        {
            // Predicted-owner scenario: SubscribeForLocalSampling is active. Reconcile will
            // replay Simulate through the subscribe path, producing the correct post-replay
            // sample. ApplyFromNetwork must NOT additionally record the raw server snapshot
            // — doing so would insert an RTT-stale value between the peer's last predicted
            // sample and the current one, visible as a brief rewind.
            _fakeNow = 0.0;
            var (binding, reactive) = CreateBinding<float>(initial: 0f);
            var bag = new DisposableBag();
            binding.SubscribeForLocalSampling(ref bag);
            // Replay seeds a first sample at t=0 with value 0.

            // Simulate reconcile: server snapshot arrives carrying a stale value (say, 999),
            // which would be visibly wrong if it became a render sample.
            _fakeNow = 0.030;
            PushSample(binding, 999f);

            // Raw .Value IS updated (reconcile needs this as baseline for replay) ...
            Assert.AreEqual(999f, reactive.Value, 1e-5f);
            // ...but the render pair should NOT include the stale server snapshot. With one
            // sample (the t=0 replay seed) and _hasPrev=false, InterpolatedValue stays at 0.
            Assert.AreEqual(0f, binding.InterpolatedValue, 1e-5f);
        }

        // ---- Coalesce window (reconcile replay) --------------------------------

        [Test]
        public void RecordSample_WritesWithinCoalesceWindow_CollapseIntoSingleCurr_PrevUnchanged()
        {
            // Reconcile replay writes many times in a single frame (<<10ms apart). With
            // coalescing, these collapse into one _curr at the final value, keeping _prev
            // pointing at the previous tick's sample — so the next render frame lerps between
            // the old _prev and the reconciled-current, NOT between two same-instant samples
            // (span→0 → clamp to _curr = no smoothing between tick events).
            var (binding, _) = CreateBinding<float>();

            _fakeNow = 0.000;
            PushSample(binding, 0f);        // first sample: _curr=0@0.000
            _fakeNow = 0.030;
            PushSample(binding, 10f);       // slide: _prev=0@0.000, _curr=10@0.030

            // Burst of replay writes within ~1ms of each other (coalesce window = 10ms).
            _fakeNow = 0.031;
            PushSample(binding, 100f);
            _fakeNow = 0.033;
            PushSample(binding, 200f);
            _fakeNow = 0.035;
            PushSample(binding, 300f);

            // _prev must still point at the 0.000 sample (0f), not at any replay intermediate.
            // _curr is the final replay value (300f), span = 0.035s (0.000 → 0.035).
            // TickRender alpha formula: (now - currTime) / span → alpha=0.5 at now=currTime+span/2.
            _fakeNow = 0.0525; // 0.035 + 0.035/2 → alpha = 0.5 → lerp(0, 300, 0.5) = 150.
            binding.TickRender(0.0);

            // Without coalescing, _prev would be 200@0.033 and span would collapse to ~2ms,
            // clamping render to 300. With coalescing, value is between 0 and 300 at the midpoint.
            Assert.Greater(binding.InterpolatedValue, 100f,
                "Coalesce must preserve the old _prev, giving non-trivial alpha at midpoint.");
            Assert.Less(binding.InterpolatedValue, 200f);
        }

        // ---- Stale bootstrap (idle gap) ----------------------------------------

        [Test]
        public void RecordSample_GapLongerThanStaleThreshold_BootstrapsAsSingleSample_DropsPrev()
        {
            // R3 ReactiveProperty suppresses identical writes — idle ticks produce no samples.
            // When motion resumes after a long idle, the stale _prev from before the idle
            // must NOT drive the render lerp: using it would make render crawl from old_pos
            // to new_pos over the huge wall-clock span (visible lag), then snap on the next
            // tick when slide re-establishes a short span. Bootstrap-as-single-sample avoids it.
            var (binding, _) = CreateBinding<float>();

            _fakeNow = 0.000;
            PushSample(binding, 0f);
            _fakeNow = 0.030;
            PushSample(binding, 10f);    // slide: _prev=0@0.000, _curr=10@0.030

            // 500ms idle, then motion resumes.
            _fakeNow = 0.530;
            PushSample(binding, 100f);   // gap 500ms > 66ms → stale bootstrap

            // Immediately after resume write: render holds _curr — no lagging _prev.
            binding.TickRender(0.0);
            Assert.AreEqual(100f, binding.InterpolatedValue, 1e-5f,
                "Stale bootstrap must drop _prev; render holds _curr until next tick.");

            // Next tick (30ms later): normal slide re-establishes the pair.
            _fakeNow = 0.560;
            PushSample(binding, 200f);
            _fakeNow = 0.575;            // currTime + span/2 = 0.560 + 0.015 → alpha = 0.5
            binding.TickRender(0.0);

            Assert.AreEqual(150f, binding.InterpolatedValue, 1e-5f,
                "After bootstrap, the next slide pairs fresh _prev/_curr for smooth lerp.");
        }

        // ---- Reset -------------------------------------------------------------

        [Test]
        public void ClearInterpolationState_AfterSubscribeAsAuthority_ApplyFromNetworkSamplesAgain()
        {
            // Ownership-transfer regression (#24): former owner had SubscribeAsAuthority
            // active, so _samplesFromSubscribe was latched true. OnLostOwnership disposes
            // the subscribe handler but previously did not clear the flag — ApplyFromNetwork
            // then saw _samplesFromSubscribe == true and skipped RecordSample, freezing
            // InterpolatedValue forever. ClearInterpolationState must reset the flag so
            // network-relayed writes sample through again.
            _fakeNow = 0.0;
            var (binding, _) = CreateBinding<float>(initial: 0f);
            var bag = new DisposableBag();
            binding.SubscribeAsAuthority(ref bag);

            // Simulate OnLostOwnership: dispose subscribe, then clear interpolation state.
            bag.Dispose();
            binding.ClearInterpolationState();

            // Now the "former owner" receives relayed snapshots from the new owner.
            _fakeNow = 1.000;
            PushSample(binding, 10f);
            _fakeNow = 1.030;
            PushSample(binding, 20f);

            _fakeNow = 1.045;
            binding.TickRender(0.0);

            // Midpoint between sample(10, 1.000) and sample(20, 1.030) at now=1.045 = 15.
            // Before the fix, ApplyFromNetwork skipped sampling → InterpolatedValue would
            // stay at 0 (the default from ClearInterpolationState).
            Assert.AreEqual(15f, binding.InterpolatedValue, 1e-5f);
        }

        [Test]
        public void ClearInterpolationState_AfterSubscribeForLocalSampling_ApplyFromNetworkSamplesAgain()
        {
            // Symmetric predicted-owner path: SubscribeForLocalSampling also latches the
            // flag. Same regression, same fix.
            _fakeNow = 0.0;
            var (binding, _) = CreateBinding<float>(initial: 0f);
            var bag = new DisposableBag();
            binding.SubscribeForLocalSampling(ref bag);

            bag.Dispose();
            binding.ClearInterpolationState();

            _fakeNow = 2.0;
            PushSample(binding, 77f);

            // First sample after clear bootstraps InterpolatedValue directly. Before the
            // fix, ApplyFromNetwork would skip RecordSample and leave it at default(0f).
            Assert.AreEqual(77f, binding.InterpolatedValue, 1e-5f);
        }

        [Test]
        public void ClearInterpolationState_ResetsPrevCurrAndInterpolatedValue()
        {
            var (binding, reactive) = CreateBinding<float>();

            _fakeNow = 1.000;
            PushSample(binding, 5f);
            _fakeNow = 1.030;
            PushSample(binding, 15f);
            _fakeNow = 1.045;
            binding.TickRender(0.0);
            Assert.AreEqual(10f, binding.InterpolatedValue, 1e-5f);

            binding.ClearInterpolationState();

            // Interpolated state is cleared...
            Assert.AreEqual(default(float), binding.InterpolatedValue);
            // ...but the raw reactive value persists (ClearInterpolationState isn't a full reset).
            Assert.AreEqual(15f, reactive.Value, 1e-5f);

            // After clearing, a new sample bootstraps cleanly (no stale _prev/_hasPrev).
            _fakeNow = 10.0;
            PushSample(binding, 42f);
            Assert.AreEqual(42f, binding.InterpolatedValue, 1e-5f);
        }
    }
}
