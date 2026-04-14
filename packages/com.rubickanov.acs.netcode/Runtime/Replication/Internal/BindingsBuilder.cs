using System;
using System.Collections.Generic;
using R3;
using Rubickanov.ACS.Runtime;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Output of a single <see cref="BindingsBuilder.Build"/> pass: the four binding
    /// arrays the replicator needs at steady state plus the derived prediction
    /// bookkeeping. Returned as a struct so the builder has one explicit seam back
    /// into the replicator rather than mutating half a dozen fields via ref.
    /// </summary>
    internal struct BindingsBuildResult
    {
        public ReplicatedFieldBinding[] Bindings;
        public AuthorityMode[] BindingAuthorities;
        public ReplicatedFieldBinding[] InterpolatedBindings;
        public ReplicatedEventBinding[] EventBindings;
        public PredictedFieldInfo[] PredictedFields;
        public int[] PredictedBindingIndices;
    }

    /// <summary>
    /// Walks the aspects on a <see cref="MonoEntity"/> and produces the binding
    /// arrays the replicator stores for the lifetime of the spawn. Owns its
    /// scratch lists internally so a replicator can keep one instance and
    /// reuse the backing allocations across spawns.
    ///
    /// Authoritative subscriptions are routed through the supplied
    /// <see cref="DisposableBag"/> refs — server-auth subscriptions land in the
    /// long-lived bag, owner-auth subscriptions in the per-ownership bag so an
    /// ownership transfer only disposes the latter.
    ///
    /// Extracted from <see cref="EntityReplicator.OnNetworkSpawn"/> so the
    /// per-aspect traversal + predicted-owner resolution is unit-testable
    /// without a NetworkManager.
    /// </summary>
    internal sealed class BindingsBuilder
    {
        // Reused across spawns to avoid the per-spawn allocation cloud.
        // Clear() at the top of Build, ToArray() at the bottom.
        private readonly List<ReplicatedFieldBinding> _bindingsScratch = new();
        private readonly List<AuthorityMode> _bindingAuthoritiesScratch = new();
        private readonly List<ReplicatedFieldBinding> _interpolatedBindingsScratch = new();
        private readonly List<ReplicatedEventBinding> _eventBindingsScratch = new();
        private readonly List<PredictedFieldInfo> _predictedFieldsScratch = new();
        private readonly List<int> _predictedBindingIndicesScratch = new();
        private readonly List<object> _aspectListScratch = new();
        private readonly HashSet<string> _predictedFieldNamesScratch = new();
        private readonly Dictionary<string, int> _aspectBindingByNameScratch = new();

        public BindingsBuildResult Build(
            MonoEntity context,
            string entityDiagnosticName,
            bool isServer,
            bool isOwner,
            double tickInterval,
            EntityReplicationSystem system,
            ref DisposableBag disposables,
            ref DisposableBag ownerDisposables)
        {
            _bindingsScratch.Clear();
            _bindingAuthoritiesScratch.Clear();
            _interpolatedBindingsScratch.Clear();
            _eventBindingsScratch.Clear();
            _predictedFieldsScratch.Clear();
            // Step 7 plumbing: binding index per predicted field. Populated alongside
            // _bindingsScratch so the index we store is the final _bindings[] index.
            _predictedBindingIndicesScratch.Clear();

            // Sort aspects by full type name so the dirty-bitmask index of each field is
            // stable between server and client, independent of the order components call
            // Context.Require<T>() in Awake(). Manual sort avoids LINQ allocations on spawn.
            _aspectListScratch.Clear();
            foreach (var a in context.GetAllAspects()) _aspectListScratch.Add(a);
            _aspectListScratch.Sort((a, b) => string.Compare(
                a.GetType().FullName, b.GetType().FullName, StringComparison.Ordinal));

            for (int ai = 0; ai < _aspectListScratch.Count; ai++)
            {
                var aspect = _aspectListScratch[ai];
                BuildForAspect(aspect, entityDiagnosticName, isServer, isOwner, tickInterval, system,
                    ref disposables, ref ownerDisposables);
            }

            return new BindingsBuildResult
            {
                Bindings = _bindingsScratch.ToArray(),
                BindingAuthorities = _bindingAuthoritiesScratch.ToArray(),
                InterpolatedBindings = _interpolatedBindingsScratch.ToArray(),
                EventBindings = _eventBindingsScratch.ToArray(),
                PredictedFields = _predictedFieldsScratch.ToArray(),
                PredictedBindingIndices = _predictedBindingIndicesScratch.ToArray(),
            };
        }

        private void BuildForAspect(
            object aspect,
            string entityDiagnosticName,
            bool isServer,
            bool isOwner,
            double tickInterval,
            EntityReplicationSystem system,
            ref DisposableBag disposables,
            ref DisposableBag ownerDisposables)
        {
            // Hoist predicted scan above the field loop so FieldBindingKind resolution knows
            // which server-auth fields the owner writes locally via ISimulate. Those fields
            // need AuthorityRenderBinding even though the owner isn't the replication
            // authority — without this, the owner's .Smooth() would render network-delayed
            // server state instead of the predicted value.
            var predictedInfos = PredictionScanner.Scan(aspect);
            _predictedFieldNamesScratch.Clear();
            if (predictedInfos.Length > 0)
            {
                for (int pi = 0; pi < predictedInfos.Length; pi++)
                    _predictedFieldNamesScratch.Add(predictedInfos[pi].Field.Name);
            }

            // Track (fieldName -> bindingIndex) for this aspect so we can join
            // PredictionScanner's output back to the exact binding that owns
            // each Predicted field. Scanners both sort by name, but a field
            // that was skipped (null reactive, type mismatch) does not become
            // a binding — the dictionary only holds entries we actually added.
            _aspectBindingByNameScratch.Clear();
            var fieldInfos = ReplicationScanner.Scan(aspect);
            foreach (var info in fieldInfos)
            {
                var reactive = info.Field.GetValue(aspect);
                if (reactive == null)
                {
                    Debug.LogError($"[EntityReplicator] Aspect '{aspect.GetType().Name}' field '{info.Field.Name}' is null on '{entityDiagnosticName}'. Initialize it in the aspect constructor or field initializer.");
                    continue;
                }
                bool isAuthority = info.Authority == AuthorityMode.Server ? isServer : isOwner;
                // Collections don't participate in prediction (scanner enforces this),
                // so predicted-owner evaluation only matters for scalar fields.
                bool isPredictedOwner = false;
                FieldBindingKind kind = FieldBindingKind.Plain;
                if (info.Kind == ReplicatedFieldKind.Scalar)
                {
                    // Predicted-owner: owner-client of a server-auth Predicted field. They run
                    // ISimulate locally each tick, so their render path needs AuthorityRender
                    // smoothing — but they are NOT the replication authority (server is), so we
                    // don't subscribe them via SubscribeAsAuthority. The !isServer guard excludes
                    // host-owner (already covered by isAuthority via isServer=true).
                    isPredictedOwner =
                        info.Authority == AuthorityMode.Server
                        && isOwner && !isServer
                        && _predictedFieldNamesScratch.Contains(info.Field.Name);

                    // "Writes locally each tick" is what AuthorityRenderBinding exists for.
                    bool writesLocally = isAuthority || isPredictedOwner;

                    kind = info.Interpolation switch
                    {
                        InterpolationMode.Linear when writesLocally => FieldBindingKind.AuthorityRendered,
                        InterpolationMode.Linear                    => FieldBindingKind.PassiveInterpolated,
                        _                                           => FieldBindingKind.Plain,
                    };
                }

                ReplicatedFieldBinding binding = info.Kind switch
                {
                    ReplicatedFieldKind.ObservableList =>
                        ReplicatedFieldBindingFactory.CreateObservableList(reactive, info.ValueType, info.Quantization, system),
                    ReplicatedFieldKind.ObservableDictionary =>
                        ReplicatedFieldBindingFactory.CreateObservableDictionary(reactive, info.KeyType!, info.ValueType, info.Quantization, system),
                    ReplicatedFieldKind.ObservableHashSet =>
                        ReplicatedFieldBindingFactory.CreateObservableHashSet(reactive, info.ValueType, info.Quantization, system),
                    ReplicatedFieldKind.ObservableRingBuffer =>
                        ReplicatedFieldBindingFactory.CreateObservableRingBuffer(reactive, info.ValueType, info.Quantization, system),
                    _ =>
                        ReplicatedFieldBindingFactory.Create(reactive, info.ValueType, kind, tickInterval, info.Quantization, system),
                };

                if (isAuthority)
                {
                    // Owner-auth subscriptions go into a separate bag so they can be
                    // disposed/re-created on ownership transfer without touching
                    // server-auth subscriptions that live for the entity's full lifetime.
                    ref var bag = ref (info.Authority == AuthorityMode.Owner ? ref ownerDisposables : ref disposables);
                    binding.SubscribeAsAuthority(ref bag);
                    // R3 ReactiveProperty.Subscribe replays the current value, so the
                    // callback fires once synthetically with _suppressNotification == false
                    // and flips OwnerWroteSinceSpawn to true before the entity has done any
                    // real work. Without this reset, initial-sync on a late-joining owner
                    // would see the flag already set and skip every server-preset owner-auth
                    // field — the exact failure mode #19 is supposed to close.
                    binding.ResetOwnerWroteSinceSpawn();
                }
                else if (isPredictedOwner)
                {
                    // Predicted-owner subscribe: sample-only, no dirty flag. Lives on
                    // ownerDisposables so it tears down on OnLostOwnership — a non-owner
                    // peer has no local writes to sample.
                    binding.SubscribeForLocalSampling(ref ownerDisposables);
                }

                _aspectBindingByNameScratch[info.Field.Name] = _bindingsScratch.Count;
                _bindingsScratch.Add(binding);
                _bindingAuthoritiesScratch.Add(info.Authority);
                if (binding.IsInterpolated)
                    _interpolatedBindingsScratch.Add(binding);
            }

            // Predicted fields are a subset of replicated fields (same attribute,
            // Predicted = true flag). PredictionScanner filters ReplicationScanner's
            // output, so the only reason a predicted field would not match here is
            // if the replicated binding was skipped (null reactive) — log and drop
            // the predicted entry rather than producing an index that writes garbage
            // on capture.
            for (int pi = 0; pi < predictedInfos.Length; pi++)
            {
                var predictedInfo = predictedInfos[pi];
                if (!_aspectBindingByNameScratch.TryGetValue(predictedInfo.Field.Name, out var bindingIndex))
                {
                    Debug.LogError($"[EntityReplicator] Aspect '{aspect.GetType().Name}' field '{predictedInfo.Field.Name}' has [Replicated(Predicted = true)] but no matching replicated binding was registered (null reactive?). Prediction snapshot will exclude this field.");
                    continue;
                }
                _predictedFieldsScratch.Add(predictedInfo);
                _predictedBindingIndicesScratch.Add(bindingIndex);
            }

            var eventInfos = ReplicationScanner.ScanEvents(aspect);
            foreach (var info in eventInfos)
            {
                var subject = info.Field.GetValue(aspect);
                if (subject == null)
                {
                    Debug.LogError($"[EntityReplicator] Aspect '{aspect.GetType().Name}' field '{info.Field.Name}' is null on '{entityDiagnosticName}'. Initialize it in the aspect constructor or field initializer.");
                    continue;
                }
                var binding = ReplicatedEventBindingFactory.Create(subject, info.ValueType, info.Authority, info.Reliability);
                _eventBindingsScratch.Add(binding);
            }
        }
    }
}
