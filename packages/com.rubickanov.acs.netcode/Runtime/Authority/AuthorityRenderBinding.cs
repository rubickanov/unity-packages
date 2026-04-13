using System;
using R3;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Replicated field binding that smooths authority-side (local) writes across frame-rate
    /// rendering. Used on peers that produce the value each network tick — the authority for
    /// server/owner-auth fields, plus predicted-owner peers that run <c>ISimulate</c> locally.
    /// Without this, <see cref="ReactivePropertyExtensions.Smooth{T}"/> would fall back to the
    /// raw <see cref="ReactiveProperty{T}.Value"/>, which updates only at tick rate; at 60+ FPS
    /// that staircases visibly (2–4 identical frames, then a jump).
    /// <para>
    /// Mechanism: every authority-side write snapshots <c>(prev ← curr, curr ← newValue)</c> and
    /// stamps wall-clock timestamps via <see cref="Clock"/>. <see cref="TickRender"/> computes
    /// <c>alpha = clamp01((now - currTime) / (currTime - prevTime))</c> and exposes
    /// <c>lerp(prev, curr, alpha)</c> as <see cref="InterpolatedValue"/>. This introduces ≈1 tick
    /// of render delay — the same tradeoff Unity's <c>NetworkTransform</c> makes — but eliminates
    /// the staircase.
    /// </para>
    /// <para>
    /// Ownership-transfer caveat: the binding kind is fixed at spawn. A peer that gains ownership
    /// of an owner-auth field mid-game keeps its previous <see cref="InterpolatedFieldBinding{T}"/>;
    /// conversely, a peer that loses ownership keeps this binding and begins receiving network
    /// snapshots via <see cref="ApplyFromNetwork"/> instead of local writes. The
    /// <see cref="ApplyFromNetwork"/> override below treats incoming snapshots as samples so
    /// rendering keeps working after a transfer — just smoothed against wall-clock rather than
    /// server-time. <see cref="ReactiveProperty{T}.Value"/> is always correct on both sides.
    /// </para>
    /// </summary>
    [Preserve]
    internal sealed class AuthorityRenderBinding<T> : ReplicatedFieldBinding<T>, IInterpolatedBinding<T>
        where T : unmanaged
    {
        // Injectable clock. Production: wall-clock (Time.unscaledTimeAsDouble). Tests swap this
        // out for deterministic time control via a fake provider. Static per-closed-generic-type,
        // so test setup needs to assign it for every T used in the fixture.
        internal static Func<double> Clock = () => Time.unscaledTimeAsDouble;

        // Writes within this wall-clock window are coalesced into _curr without sliding _prev.
        // Rationale: on a predicted-owner clients a single tick can produce many RecordSample
        // calls — reconcile replay runs Simulate N times, each write firing the subscribe
        // callback. Without coalescing, _prev and _curr collapse to the same wall-clock
        // instant, span→0, TickRender holds _curr, and render stalls between tick batches
        // (visible jitter). Sized as 0.3 × tickDelta — well below one tick interval
        // (so legitimate consecutive ticks slide instead of coalescing) but orders of
        // magnitude above intra-frame write spread (<1ms). At 30 Hz: 0.3 × 33ms ≈ 10ms.
        private readonly double _coalesceWindowSeconds;

        // Writes that arrive more than this far apart are treated as bootstrap (i.e. no
        // meaningful _prev). Rationale: R3 ReactiveProperty suppresses identical writes, so
        // idle ticks (input == zero → delta == zero → Value unchanged) produce no samples.
        // When motion resumes after an idle gap, the stale _prev from before the idle no
        // longer represents "one tick ago" — it's stuck at the last-movement wall time,
        // often seconds behind. Using it would make render alpha grow very slowly over the
        // huge span (visible lag) and the following tick's slide would snap visibly.
        // Instead we reset to single-sample bootstrap: render holds _curr until the next
        // non-idle tick reestablishes a fresh _prev/_curr pair with the normal tick span.
        // Sized as 2.5 × tickDelta — any realistic motion streak produces writes much
        // faster than this (one per tick), so continuous motion never falls into the
        // bootstrap branch. At 30 Hz: 2.5 × 33ms ≈ 83ms.
        private readonly double _staleSampleThresholdSeconds;

        private readonly Lerp<T> _lerp;
        private T _prev;
        private T _curr;
        private T _interpolatedValue;
        private double _prevTime;
        private double _currTime;
        private bool _hasPrev;
        private bool _hasCurr;
        // True once a subscribe handler has been attached that will feed samples on every
        // write to the reactive. When true, ApplyFromNetwork must NOT call RecordSample
        // itself — reconcile will replay inputs through Simulate, whose writes fire the
        // subscribe handler and produce the authoritative post-replay sample. Double-sampling
        // (once from the server snapshot, again from replay) would interleave an old
        // server-delayed value with the predicted current value and visibly jitter.
        private bool _samplesFromSubscribe;

        public override bool IsInterpolated => true;
        public T InterpolatedValue => _interpolatedValue;

        public AuthorityRenderBinding(ReactiveProperty<T> reactive, Lerp<T> lerp, double tickDelta)
            : base(reactive)
        {
            _lerp = lerp;
            // Track tick rate so the coalesce / stale thresholds scale with it instead of
            // assuming 30 Hz. See ISSUES.md #23.
            _coalesceWindowSeconds = 0.3 * tickDelta;
            _staleSampleThresholdSeconds = 2.5 * tickDelta;
            InterpolationRegistry<T>.Register(reactive, this);
        }

        /// <summary>
        /// Authority path: the local peer is the replication authority. Subscribes to the
        /// underlying reactive so every write marks dirty (for relay) AND records a render
        /// sample. The R3 <c>Subscribe</c> call replays the current value synchronously, which
        /// bootstraps <see cref="_curr"/> with the field's initial value.
        /// </summary>
        public override void SubscribeAsAuthority(ref DisposableBag disposables)
        {
            _samplesFromSubscribe = true;
            _reactive.Subscribe(value =>
            {
                if (_suppressNotification) return;
                IsDirty = true;
                _ownerWroteSinceSpawn = true;
                RecordSample(value);
            }).AddTo(ref disposables);
        }

        /// <summary>
        /// Predicted-owner path: the local peer runs <c>ISimulate</c> but is NOT the replication
        /// authority (server is). We want the local writes to drive render sampling, but we must
        /// NOT flip <see cref="ReplicatedFieldBinding.IsDirty"/> — that would trigger an incorrect
        /// owner-auth relay on a server-auth field.
        /// </summary>
        public override void SubscribeForLocalSampling(ref DisposableBag disposables)
        {
            _samplesFromSubscribe = true;
            _reactive.Subscribe(value =>
            {
                if (_suppressNotification) return;
                RecordSample(value);
            }).AddTo(ref disposables);
        }

        /// <summary>
        /// Incoming network snapshot.
        /// <para>
        /// When a subscribe-sampler is attached (authority or predicted-owner), we intentionally
        /// skip <see cref="RecordSample"/> here: on a predicted-owner, reconcile runs right after
        /// this and replays <c>Simulate</c> through the subscribe path, producing the correct
        /// post-replay sample. Sampling the raw server snapshot first would insert a value that is
        /// one RTT behind the peer's predicted state — visible as a brief rewind before replay
        /// catches up. On an ownership-gain authority that never previously owned the entity,
        /// the first local write after <see cref="ResetOwnerWroteSinceSpawn"/> replays through
        /// Subscribe anyway.
        /// </para>
        /// <para>
        /// When no subscribe-sampler exists (e.g. former owner after an ownership transfer who is
        /// now receiving broadcasts without running Simulate), this is the only sampling path —
        /// treat the snapshot as a sample so the smoothed view keeps moving. Degraded vs. true
        /// server-time interpolation (wall-clock span between snapshots instead of server-tick
        /// timestamps) but strictly better than frozen.
        /// </para>
        /// </summary>
        public override void ApplyFromNetwork(double receivedTime)
        {
            if (!_hasPendingValue) return;
            if (!_samplesFromSubscribe)
                RecordSample(_pendingValue);
            WriteSuppressed(_pendingValue);
            _hasPendingValue = false;
        }

        public override void TickRender(double renderTime)
        {
            // renderTime is server-time-based and calibrated for InterpolatedFieldBinding's
            // snapshot ring. We intentionally ignore it here — authority smoothing uses wall
            // clock because local writes are not stamped with server ticks.
            if (!_hasCurr) return;
            if (!_hasPrev)
            {
                _interpolatedValue = _curr;
                return;
            }

            double span = _currTime - _prevTime;
            if (span <= 1e-9)
            {
                _interpolatedValue = _curr;
                return;
            }

            // Target a render time one span behind wall-clock: at the instant of a new write
            // (now == currTime) we render _prev (α=0); one span later (just before the next
            // write) we render _curr (α=1). This hides the tick-rate staircase at the cost of
            // ≈1 tick of render delay.
            double now = Clock();
            double raw = (now - _currTime) / span;
            float alpha = raw <= 0.0 ? 0f : raw >= 1.0 ? 1f : (float)raw;
            _interpolatedValue = _lerp(_prev, _curr, alpha);
        }

        public override void ClearInterpolationState()
        {
            _hasPrev = false;
            _hasCurr = false;
            _prev = default;
            _curr = default;
            _interpolatedValue = default;
            _prevTime = 0;
            _currTime = 0;
            // Reset so ApplyFromNetwork is free to sample again after a lifecycle
            // event that tore down the subscribe handler (e.g. OnLostOwnership
            // disposes _ownerDisposables, removing the Subscribe-side sampler).
            // Without this, a former owner's InterpolatedValue freezes on the
            // last local write even as the new owner's writes relay in.
            _samplesFromSubscribe = false;
        }

        public override void OnDespawn()
        {
            InterpolationRegistry<T>.Unregister(_reactive);
        }

        private void RecordSample(T value)
        {
            double now = Clock();
            if (!_hasCurr)
            {
                _curr = value;
                _currTime = now;
                _interpolatedValue = value;
                _hasCurr = true;
                return;
            }

            double gap = now - _currTime;

            // Coalesce writes that land within the same tick boundary. Reconcile replay
            // runs Simulate N times in a single frame — each call writes .Value — but the
            // only value that matters for rendering is the final post-replay state. Without
            // coalescing, _prev/_curr collapse to the same wall-clock instant, span→0, and
            // TickRender holds _curr between tick events (visible stall + snap = jitter).
            if (gap < _coalesceWindowSeconds)
            {
                _curr = value;
                _currTime = now;
                return;
            }

            // Gap > stale threshold → previous _curr is too old to serve as _prev. Treat
            // this write as a fresh bootstrap: drop _hasPrev so TickRender holds _curr
            // until the next tick delivers a real "previous" sample. Avoids the "render
            // crawls from stale _prev to fresh _curr" jerk when motion resumes after idle.
            if (gap > _staleSampleThresholdSeconds)
            {
                _curr = value;
                _currTime = now;
                _interpolatedValue = value;
                _hasPrev = false;
                return;
            }

            _prev = _curr;
            _prevTime = _currTime;
            _curr = value;
            _currTime = now;
            _hasPrev = true;
        }
    }
}
