using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Wires a replicator to its per-TInput <see cref="PredictionManager{T}"/>.
    /// Resolves the TInput by walking the entity's MonoBehaviour tree for an
    /// <see cref="ISimulate{T}"/> implementation, then obtains a typed
    /// <see cref="IAspectPredictionHook"/> through a single reflective Invoke
    /// (cached per TInput). After Bootstrap every reconcile call flows through
    /// a direct virtual on the stored interface — zero reflection, zero
    /// allocation on the hot path.
    ///
    /// Extracted from EntityReplicator so the resolution + caching surface
    /// lives behind one seam rather than bleeding through the replicator.
    /// </summary>
    internal sealed class PredictionBinder
    {
        private readonly EntityReplicator _owner;
        private readonly NetworkObject _root;
        private readonly List<MonoBehaviour> _behavioursScratch = new();

        private Type? _inputType;
        private IAspectPredictionHook? _hook;

        /// <summary>
        /// The ISimulate&lt;T&gt; TInput resolved at Bootstrap. Null when no ISimulate
        /// implementor was found under the root NetworkObject, or when the
        /// PredictionManager refused to build (see <see cref="PredictionManager{T}.GetOrCreate"/>).
        /// </summary>
        public Type? InputType => _inputType;

        public PredictionBinder(EntityReplicator owner, NetworkObject root)
        {
            _owner = owner;
            _root = root;
        }

        /// <summary>
        /// Resolve TInput and register with the typed prediction manager.
        /// Gate on ISimulate rather than on Predicted-field count — the pipeline
        /// is needed whenever a component runs tick-driven authoritative logic,
        /// independent of whether any field opts into snapshot+reconcile.
        /// Predicted fields without an ISimulate consumer is a programmer error
        /// and is surfaced here.
        /// </summary>
        public void Bootstrap(int predictedFieldCount, NetworkManager nm, string ownerDiagnosticName)
        {
            _inputType = ResolveInputType();
            if (_inputType == null)
            {
                if (predictedFieldCount > 0)
                    Debug.LogError($"[EntityReplicator] '{ownerDiagnosticName}' has Predicted fields but no component implements ISimulate<TInput>. Prediction disabled for this entity.");
                return;
            }

            // GetOrCreate can refuse to build a manager — today only when TickRate is 0
            // (see PredictionManager.GetOrCreate). On refusal the replicator's predicted
            // fields go unsubscribed, which matches the rest of the early-return contract
            // in OnNetworkSpawn.
            var hook = PredictionHookCache.Resolve(_inputType, nm);
            if (hook == null)
            {
                _inputType = null;
                return;
            }
            _hook = hook;
            hook.Register(_owner);
        }

        public void Unregister()
        {
            if (_hook != null)
            {
                _hook.Unregister(_owner);
                _hook = null;
            }
            _inputType = null;
        }

        public void OnServerStateApplied(int serverTick)
        {
            _hook?.OnServerStateApplied(_owner, serverTick);
        }

        // Prefab-level cache for ResolveInputType. Keyed by NetworkObject.PrefabIdHash —
        // stable per-prefab — so a spawn wave of identical prefabs does the reflection
        // walk once. PrefabIdHash is 0 for non-prefab (scene-placed) NetworkObjects —
        // we skip the cache in that case so unrelated scene objects don't collide on key 0.
        private static readonly Dictionary<uint, Type?> s_InputTypeCache = new();

        // Play-Mode-without-Domain-Reload safety: clear static caches on subsystem
        // registration. Matching clear lives on the nested PredictionHookCache.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_InputTypeCache.Clear();
        }

        // Find the first ISimulate<T> implemented by any MonoBehaviour under the root
        // NetworkObject (stopping at nested NetworkObject boundaries, same rule
        // NetworkScopeController uses). The returned generic argument is the TInput
        // this entity's prediction pipeline runs on.
        private Type? ResolveInputType()
        {
            uint prefabHash = _root.PrefabIdHash;
            bool useCache = prefabHash != 0;
            if (useCache && s_InputTypeCache.TryGetValue(prefabHash, out var cached))
                return cached;

            _behavioursScratch.Clear();
            _root.GetComponentsInChildren(includeInactive: true, _behavioursScratch);

            Type? resolved = null;
            for (int i = 0; i < _behavioursScratch.Count; i++)
            {
                var behaviour = _behavioursScratch[i];
                if (behaviour == null) continue;
                // Walk up the transform chain to the closest NetworkObject — same
                // rule GetComponentInParent<NetworkObject>() enforces, but alloc-free
                // (TryGetComponent does not allocate, whereas GetComponentInParent
                // walks and materializes an array on each call).
                if (!BelongsToNetworkObject(behaviour.transform, _root)) continue;

                var interfaces = behaviour.GetType().GetInterfaces();
                for (int j = 0; j < interfaces.Length; j++)
                {
                    var iface = interfaces[j];
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(ISimulate<>))
                    {
                        resolved = iface.GetGenericArguments()[0];
                        break;
                    }
                }
                if (resolved != null) break;
            }

            if (useCache) s_InputTypeCache[prefabHash] = resolved;
            return resolved;
        }

        // Walks up the transform chain and returns true iff the first NetworkObject
        // encountered is the same instance as `target`. Mirrors the semantics of
        // `behaviour.GetComponentInParent<NetworkObject>() == target` without that
        // method's per-call allocation.
        private static bool BelongsToNetworkObject(Transform t, NetworkObject target)
        {
            for (var cur = t; cur != null; cur = cur.parent)
            {
                if (cur.TryGetComponent<NetworkObject>(out var no))
                    return no == target;
            }
            return false;
        }

        // Resolves PredictionManager<TInput> instances into a non-generic
        // IAspectPredictionHook view so EntityReplicator (non-generic) can hold
        // a typed reference after one reflective Invoke. Cached MethodInfo keeps
        // the per-Register cost to a single Invoke + single object[]. All
        // subsequent Register / Unregister / OnServerStateApplied calls flow
        // through a direct virtual call on the stored interface reference —
        // zero reflection, zero allocation on the reconcile hot path.
        private static class PredictionHookCache
        {
            private static readonly Dictionary<Type, MethodInfo> s_GetOrCreate = new();

            // Play-Mode-without-Domain-Reload safety: clear static caches on subsystem registration.
            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void ResetStatics()
            {
                s_GetOrCreate.Clear();
            }

            public static IAspectPredictionHook? Resolve(Type tInput, NetworkManager nm)
            {
                if (!s_GetOrCreate.TryGetValue(tInput, out var mi))
                {
                    var managerType = typeof(PredictionManager<>).MakeGenericType(tInput);
                    mi = managerType.GetMethod("GetOrCreate", BindingFlags.NonPublic | BindingFlags.Static)
                        ?? throw new InvalidOperationException("PredictionManager.GetOrCreate not found");
                    s_GetOrCreate[tInput] = mi;
                }

                // GetOrCreate returns null when TickRate == 0. Cast through the shared
                // interface — PredictionManager<TInput> implements IAspectPredictionHook
                // so this is a plain reference conversion.
                return (IAspectPredictionHook?)mi.Invoke(null, new object?[] { nm });
            }
        }
    }
}
