using System.Collections.Generic;
using R3;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode
{
    [DisallowMultipleComponent]
    public class AspectReplicator : NetworkBehaviour
    {
        private ReplicatedFieldBinding[] _bindings = null!;
        private DisposableBag _disposables;

        public override void OnNetworkSpawn()
        {
            Debug.Log($"[AspectReplicator] OnNetworkSpawn. IsServer={IsServer}, IsClient={IsClient}, IsOwner={IsOwner}");

            var context = GetComponent<EntityContext>();
            var allBindings = new List<ReplicatedFieldBinding>();

            foreach (var aspect in context.GetAllAspects())
            {
                var fieldInfos = ReplicationScanner.Scan(aspect);
                Debug.Log($"[AspectReplicator] Aspect {aspect.GetType().Name}: {fieldInfos.Length} replicated fields");

                foreach (var info in fieldInfos)
                {
                    var reactive = info.Field.GetValue(aspect);
                    var binding = ReplicatedFieldBindingFactory.Create(reactive, info.ValueType);

                    bool isAuthority = info.Authority == AuthorityMode.Server ? IsServer : IsOwner;
                    if (isAuthority)
                        binding.SubscribeAsAuthority(ref _disposables);

                    allBindings.Add(binding);
                }
            }

            _bindings = allBindings.ToArray();
            Debug.Log($"[AspectReplicator] Total bindings: {_bindings.Length}");

            if (IsServer)
                NetworkManager.NetworkTickSystem.Tick += OnServerTick;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
                NetworkManager.NetworkTickSystem.Tick -= OnServerTick;

            _disposables.Dispose();
        }

        private void OnServerTick()
        {
            // Build dirty mask
            ulong dirtyMask = 0;
            for (int i = 0; i < _bindings.Length; i++)
            {
                if (_bindings[i].IsDirty)
                    dirtyMask |= 1UL << i;
            }

            if (dirtyMask == 0) return;

            Debug.Log($"[AspectReplicator] Sending dirty mask: {dirtyMask}");

            var writer = new FastBufferWriter(256, Allocator.Temp, int.MaxValue);
            try
            {
                writer.WriteValueSafe(dirtyMask);

                for (int i = 0; i < _bindings.Length; i++)
                {
                    if ((dirtyMask & (1UL << i)) != 0)
                    {
                        _bindings[i].WriteTo(writer);
                        _bindings[i].ClearDirty();
                    }
                }

                BroadcastStateRpc(writer.ToArray());
            }
            finally
            {
                writer.Dispose();
            }
        }

        [Rpc(SendTo.NotServer)]
        private void BroadcastStateRpc(byte[] payload)
        {
            // Host already has the authoritative state, skip applying
            if (IsHost) return;

            Debug.Log($"[AspectReplicator] RPC received, payload size: {payload.Length}");

            var reader = new FastBufferReader(payload, Allocator.Temp);
            try
            {
                reader.ReadValueSafe(out ulong dirtyMask);

                for (int i = 0; i < _bindings.Length; i++)
                {
                    if ((dirtyMask & (1UL << i)) != 0)
                    {
                        _bindings[i].ReadFrom(reader);
                        _bindings[i].ApplyFromNetwork();
                    }
                }
            }
            finally
            {
                reader.Dispose();
            }
        }
    }
}
