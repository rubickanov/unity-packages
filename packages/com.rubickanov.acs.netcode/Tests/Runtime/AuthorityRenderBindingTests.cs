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
        // Timing model: sample logic is driven through ApplyFromNetwork / PushSnapshot in
        // most tests, which routes directly into RecordSample(value) using _fakeNow as the
        // sample timestamp. SubscribeAsAuthority/SubscribeForLocalSampling pathways are
        // tested in dedicated cases because R3's Subscribe replays the current value
        // synchronously (bootstrap sample) and `Value = x; Value = x` is suppressed, both
        // of which would complicate generic two-sample tests.
        //
        // Sample-gap ranges that matter:
        //   < 10ms   → coalesce into _curr (intra-frame reconcile writes).
        //   10..66ms → slide pair (normal tick-to-tick motion @ 30–100 Hz).
        //   > 66ms   → stale bootstrap (idle gap; drop _prev, hold _curr).
        // Tests that want slide behavior use a 30ms step (mid of range).

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
            var binding = (AuthorityRenderBinding<T>)
                ReplicatedFieldBindingFactory.Create(reactive, typeof(T), FieldBindingKind.AuthorityRendered);
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

        // ---- Lerp between samples ----------------------------------------------

        [Test]
        public void TwoSamples_NowAtCurrTime_AlphaZero_RendersPrev()
        {
            var (binding, _) = CreateBinding<float>();

            _fakeNow = 1.0;
            PushSample(binding, 0f);
            _fakeNow = 2.0;
            PushSample(binding, 10f);

            // now == _currTime → (2-2)/1 = 0 → lerp(prev, curr, 0) = prev.
            binding.TickRender(0.0);

            Assert.AreEqual(0f, binding.InterpolatedValue, 1e-5f);
        }

        [Test]
        public void TwoSamples_NowAtMidpoint_RendersMidpoint()
        {
            var (binding, _) = CreateBinding<float>();

            _fakeNow = 1.0;
            PushSample(binding, 0f);
            _fakeNow = 2.0;
            PushSample(binding, 10f);

            // span = 1, now = 2.5 → (2.5-2)/1 = 0.5 → lerp(0,10,0.5) = 5.
            _fakeNow = 2.5;
            binding.TickRender(0.0);

            Assert.AreEqual(5f, binding.InterpolatedValue, 1e-5f);
        }

        [Test]
        public void TwoSamples_NowBeyondOneSpan_ClampsAtCurr()
        {
            var (binding, _) = CreateBinding<float>();

            _fakeNow = 1.0;
            PushSample(binding, 0f);
            _fakeNow = 2.0;
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

            _fakeNow = 1.0;
            PushSample(binding, 0f);
            _fakeNow = 2.0;
            PushSample(binding, 10f);

            // now < currTime can only happen if TickRender runs before wall-clock advances past
            // the write — raw < 0 → clamp to 0 → prev.
            _fakeNow = 1.5;
            binding.TickRender(0.0);

            Assert.AreEqual(0f, binding.InterpolatedValue, 1e-5f);
        }

        // ---- Sliding pair ------------------------------------------------------

        [Test]
        public void ThirdSample_SlidesPair_PrevBecomesSecondSample()
        {
            var (binding, _) = CreateBinding<float>();

            _fakeNow = 1.0;
            PushSample(binding, 100f);
            _fakeNow = 2.0;
            PushSample(binding, 200f);
            _fakeNow = 3.0;
            PushSample(binding, 300f);   // pair is now (_prev=200@t=2, _curr=300@t=3).

            _fakeNow = 3.5;
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
            PushSample(binding, 7f);   // second write at identical _fakeNow → span collapses to 0.

            _fakeNow = 1.5;
            binding.TickRender(0.0);

            // span <= 1e-9 → implementation holds _curr instead of dividing by ~zero.
            Assert.AreEqual(7f, binding.InterpolatedValue, 1e-5f);
        }

        // ---- Vector3 / Quaternion lerpers --------------------------------------

        [Test]
        public void TwoSamples_Vector3_LerpsAtMidpoint()
        {
            var (binding, _) = CreateBinding<Vector3>();

            _fakeNow = 0.0;
            PushSample(binding, Vector3.zero);
            _fakeNow = 1.0;
            PushSample(binding, new Vector3(10f, 20f, 30f));

            _fakeNow = 1.5;
            binding.TickRender(0.0);

            Assert.AreEqual(5f, binding.InterpolatedValue.x, 1e-5f);
            Assert.AreEqual(10f, binding.InterpolatedValue.y, 1e-5f);
            Assert.AreEqual(15f, binding.InterpolatedValue.z, 1e-5f);
        }

        [Test]
        public void TwoSamples_Quaternion_MidpointPreservesUnitLength()
        {
            var (binding, _) = CreateBinding(Quaternion.identity);

            _fakeNow = 0.0;
            PushSample(binding, Quaternion.identity);
            _fakeNow = 1.0;
            PushSample(binding, Quaternion.Euler(0f, 90f, 0f));

            _fakeNow = 1.5;
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
            _fakeNow = 0.0;
            var (binding, reactive) = CreateBinding<float>(initial: 0f);
            var bag = new DisposableBag();
            binding.SubscribeAsAuthority(ref bag);
            binding.ClearDirty();

            _fakeNow = 1.0;
            reactive.Value = 10f;   // replay gave sample(0,0); this is sample(10,1).

            _fakeNow = 1.5;
            binding.TickRender(0.0);

            // Midpoint between sample(0,0) and sample(10,1) at now=1.5 → span=1, raw=(1.5-1)/1=0.5.
            Assert.AreEqual(5f, binding.InterpolatedValue, 1e-5f);
            Assert.IsTrue(binding.IsDirty, "Authority write must mark the binding dirty for relay.");
        }

        [Test]
        public void SubscribeForLocalSampling_RecordsSample_WithoutMarkingDirty()
        {
            _fakeNow = 0.0;
            var (binding, reactive) = CreateBinding<float>(initial: 0f);

            var bag = new DisposableBag();
            binding.SubscribeForLocalSampling(ref bag);
            // Replay at _fakeNow=0 gave sample(0,0); subscribing must NOT mark dirty.
            Assert.IsFalse(binding.IsDirty, "Local-sampling subscribe must not mark dirty on replay.");

            _fakeNow = 1.0;
            reactive.Value = 10f;

            _fakeNow = 1.5;
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
                reactive, typeof(int), FieldBindingKind.AuthorityRendered);

            Assert.IsInstanceOf<ReplicatedFieldBinding<int>>(binding);
            Assert.IsNotInstanceOf<AuthorityRenderBinding<int>>(binding);
            Assert.IsFalse(binding.IsInterpolated);
        }

        // ---- .Smooth() integration ---------------------------------------------

        [Test]
        public void Smooth_ReturnsInterpolatedValue_NotRawValue()
        {
            var (binding, reactive) = CreateBinding<float>();

            _fakeNow = 1.0;
            PushSample(binding, 0f);
            _fakeNow = 2.0;
            PushSample(binding, 10f);

            _fakeNow = 2.5;
            binding.TickRender(0.0);

            Assert.AreEqual(5f, reactive.Smooth(), 1e-5f);
            // Raw value is the latest sample (10f), not the smoothed midpoint.
            Assert.AreEqual(10f, reactive.Value, 1e-5f);
        }

        [Test]
        public void OnDespawn_UnregistersFromInterpolationRegistry_SmoothFallsBackToValue()
        {
            var (binding, reactive) = CreateBinding<float>();

            _fakeNow = 1.0;
            PushSample(binding, 0f);
            _fakeNow = 2.0;
            PushSample(binding, 10f);
            _fakeNow = 2.5;
            binding.TickRender(0.0);
            Assert.AreEqual(5f, reactive.Smooth(), 1e-5f);

            binding.OnDespawn();
            _bindings.Remove(binding); // avoid double-OnDespawn from TearDown

            // Post-despawn Smooth() must return .Value (10f), not the cached 5f.
            Assert.AreEqual(10f, reactive.Smooth(), 1e-5f);
        }

        // ---- ApplyFromNetwork writes raw value ---------------------------------

        [Test]
        public void ApplyFromNetwork_WritesRawValueToReactive_ForOwnershipTransferCase()
        {
            // After ownership transfer, the former owner starts receiving network snapshots
            // via ApplyFromNetwork. No subscribe-sampler is attached, so ApplyFromNetwork is
            // the only sampling path — it must both write .Value and feed the render pair.
            var (binding, reactive) = CreateBinding<float>();

            _fakeNow = 1.0;
            PushSample(binding, 100f);
            Assert.AreEqual(100f, reactive.Value, 1e-5f);

            _fakeNow = 2.0;
            PushSample(binding, 200f);
            Assert.AreEqual(200f, reactive.Value, 1e-5f);

            _fakeNow = 2.5;
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
            _fakeNow = 1.0;
            PushSample(binding, 999f);

            // Raw .Value IS updated (reconcile needs this as baseline for replay) ...
            Assert.AreEqual(999f, reactive.Value, 1e-5f);
            // ...but the render pair should NOT include the stale server snapshot. With one
            // sample (the t=0 replay seed) and _hasPrev=false, InterpolatedValue stays at 0.
            Assert.AreEqual(0f, binding.InterpolatedValue, 1e-5f);
        }

        [Test]
        public void RecordSample_WritesWithinCoalesceWindow_CollapseIntoSingleCurr_PrevUnchanged()
        {
            // Reconcile replay writes many times in a single frame (<<10ms apart). With
            // coalescing, these collapse into one _curr at the final value, keeping _prev
            // pointing at the previous sample — so the next render frame lerps between the
            // old _prev and the reconciled-current, NOT between two same-instant samples
            // (span=0 → clamp to _curr = no smoothing between tick events).
            var (binding, _) = CreateBinding<float>();

            _fakeNow = 0.0;
            PushSample(binding, 0f);       // first sample: _curr=0@0.0

            // Burst of replay writes, all within the 10ms coalesce window. Each should
            // overwrite _curr without touching _prev/_prevTime/_hasPrev.
            _fakeNow = 1.000;
            PushSample(binding, 10f);      // slide: _prev=0@0.0, _curr=10@1.000
            _fakeNow = 1.001;
            PushSample(binding, 100f);     // coalesce: _curr=100@1.001
            _fakeNow = 1.003;
            PushSample(binding, 200f);     // coalesce: _curr=200@1.003
            _fakeNow = 1.005;
            PushSample(binding, 300f);     // coalesce: _curr=300@1.005

            // After the burst: _prev=0@0.0, _curr=300@1.005, span ≈ 1.005s.
            // At midpoint of span → alpha ≈ 0.5 → value around 150.
            _fakeNow = 1.005 + 0.5 * 1.005; // = 1.5075
            binding.TickRender(0.0);

            // Without coalescing, _prev would have been updated to 200@1.003 and span would
            // collapse to ~0.002s → TickRender would clamp to _curr (300). With coalescing,
            // rendered value is strictly between _prev (0) and _curr (300).
            Assert.Greater(binding.InterpolatedValue, 100f,
                "Coalesce must preserve the old _prev, giving non-trivial alpha at midpoint.");
            Assert.Less(binding.InterpolatedValue, 200f);
        }

        // ---- Reset -------------------------------------------------------------

        [Test]
        public void ClearInterpolationState_ResetsPrevCurrAndInterpolatedValue()
        {
            var (binding, reactive) = CreateBinding<float>();

            _fakeNow = 1.0;
            PushSample(binding, 5f);
            _fakeNow = 2.0;
            PushSample(binding, 15f);
            _fakeNow = 2.5;
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
