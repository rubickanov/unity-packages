using System;
using System.Collections.Generic;
using Rubickanov.ACS.Runtime;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Honors <c>[NetworkScope]</c> on <see cref="IEntityComponent"/> siblings of the
    /// root <see cref="NetworkObject"/>. Disables ServerOnly components on non-server
    /// peers and OwnerOnly components on non-owner peers; tracks the owner-scoped set
    /// so ownership transfers can re-toggle them without re-walking the hierarchy.
    ///
    /// Extracted from EntityReplicator so the scope-disabling pass owns its own scratch
    /// buffers and state rather than bleeding them into the replicator.
    /// </summary>
    internal sealed class NetworkScopeController
    {
        private readonly NetworkObject _root;
        private readonly List<IEntityComponent> _scopeComponentsBuffer = new();
        private readonly List<Behaviour> _ownerScopedScratch = new();
        private Behaviour[] _ownerScopedComponents = Array.Empty<Behaviour>();

        public NetworkScopeController(NetworkObject root)
        {
            _root = root;
        }

        /// <summary>
        /// Initial scope pass at spawn. Walks every IEntityComponent under the root
        /// NetworkObject (stopping at nested NetworkObject boundaries), disables
        /// ServerOnly on non-server peers and OwnerOnly on non-owner peers, and caches
        /// the OwnerOnly set for <see cref="ReapplyOwner"/>.
        /// </summary>
        public void ApplyInitial(bool isServer, bool isOwner)
        {
            _scopeComponentsBuffer.Clear();
            _root.GetComponentsInChildren(includeInactive: true, _scopeComponentsBuffer);
            _ownerScopedScratch.Clear();

            for (int i = 0; i < _scopeComponentsBuffer.Count; i++)
            {
                var component = _scopeComponentsBuffer[i];

                // Skip any IEntityComponent that is not a Behaviour (pure C# or otherwise).
                if (component is not Behaviour behaviour) continue;

                // Stop at nested NetworkObject boundaries. Children that belong to a
                // different NetworkObject must not be scope-managed by this controller.
                if (behaviour.GetComponentInParent<NetworkObject>() != _root)
                {
                    // A user who put [NetworkScope] on a component living inside a nested
                    // NetworkObject would otherwise see the attribute silently ignored — flag
                    // it so they either move the component up or scope it on the nested NO.
                    var nestedScope = NetworkScopeScanner.GetScope(component.GetType());
                    if (nestedScope != NetworkScope.Everywhere)
                    {
                        Debug.LogWarning(
                            $"[NetworkScopeController] {component.GetType().Name} on '{behaviour.gameObject.name}' " +
                            $"is marked [NetworkScope({nestedScope})] but sits under a nested NetworkObject — " +
                            $"its scope is NOT applied by the parent replicator. Move the component to the root " +
                            $"NetworkObject, or attach an EntityReplicator to the nested NetworkObject.");
                    }
                    continue;
                }

                var scope = NetworkScopeScanner.GetScope(component.GetType());
                if (scope == NetworkScope.Everywhere) continue;

                if (scope == NetworkScope.ServerOnly)
                {
                    behaviour.enabled = isServer;
                }
                else // OwnerOnly
                {
                    behaviour.enabled = isOwner;
                    _ownerScopedScratch.Add(behaviour);
                }
            }

            _ownerScopedComponents = _ownerScopedScratch.Count > 0
                ? _ownerScopedScratch.ToArray()
                : Array.Empty<Behaviour>();
        }

        /// <summary>
        /// Re-toggle cached OwnerOnly components after an ownership transfer.
        /// </summary>
        public void ReapplyOwner(bool isOwner)
        {
            for (int i = 0; i < _ownerScopedComponents.Length; i++)
                _ownerScopedComponents[i].enabled = isOwner;
        }
    }
}
