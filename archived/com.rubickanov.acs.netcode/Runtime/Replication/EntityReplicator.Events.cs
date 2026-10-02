using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode
{
    // Event-dispatch surface: per-event index routing for server-broadcast and
    // owner-submitted events, plus the authority-side subscribe helper that wires
    // Subject<T> writes to the outbound pipeline. Split off the main partial so
    // the lifecycle file stays focused; shared private state stays on the main partial.
    public partial class EntityReplicator
    {
        /// <summary>
        /// Client-side: dispatch an incoming event from the server broadcast.
        /// </summary>
        internal void DispatchEvent(byte eventIndex, FastBufferReader reader)
        {
            // Host already fired the Subject locally on the authority side — skip to avoid double-apply.
            if (IsHost) return;

            if (eventIndex >= _eventBindings.Length)
            {
                Debug.LogError($"[EntityReplicator] Event index {eventIndex} out of range ({_eventBindings.Length} bindings) on '{gameObject.name}'.");
                return;
            }

            var binding = _eventBindings[eventIndex];

            // Pure client owner: it is authority for this owner-auth event and has already
            // fired the Subject locally at user-write time. The server relay is a duplicate.
            if (IsOwner && binding.Authority == AuthorityMode.Owner) return;

            binding.ApplyFromNetwork(reader);
        }

        /// <summary>
        /// Server-side: handle an owner-submitted event — validate, relay, and fire locally.
        /// </summary>
        internal void HandleOwnerEvent(byte eventIndex, FastBufferReader reader, IEventBroadcaster broadcaster)
        {
            if (eventIndex >= _eventBindings.Length)
            {
                Debug.LogError($"[EntityReplicator] Owner event index {eventIndex} out of range ({_eventBindings.Length} bindings) on '{gameObject.name}'.");
                return;
            }

            var binding = _eventBindings[eventIndex];
            if (binding.Authority != AuthorityMode.Owner)
            {
                Debug.LogWarning($"[EntityReplicator] Owner submitted server-auth event index {eventIndex} on '{gameObject.name}'. Dropping.");
                return;
            }

            // Read the event payload, relay to clients, and fire locally on the server.
            int payloadSize = binding.PayloadSize;
            unsafe
            {
                byte* temp = stackalloc byte[payloadSize];
                reader.ReadBytesSafe(temp, payloadSize);

                // Build relay message for other clients.
                var relayWriter = new FastBufferWriter(sizeof(ulong) + sizeof(byte) + payloadSize, Allocator.Temp);
                try
                {
                    relayWriter.WriteValueSafe(NetworkObjectId);
                    relayWriter.WriteValueSafe(eventIndex);
                    relayWriter.WriteBytesSafe(temp, payloadSize);

                    broadcaster.SendEvent(NetworkObjectId, eventIndex, relayWriter,
                        binding.Authority, binding.Reliability, isOwnerSubmit: false);
                }
                finally
                {
                    relayWriter.Dispose();
                }

                // Fire locally on the server so server-side listeners see the event.
                // Wrap the stackalloc buffer directly — Allocator.None means no copy,
                // reader does not own the pointer, Dispose is a no-op on the buffer.
                var localReader = new FastBufferReader(temp, Allocator.None, payloadSize);
                try
                {
                    binding.ApplyFromNetwork(localReader);
                }
                finally
                {
                    localReader.Dispose();
                }
            }
        }

        // Spawn-time subscribe: wires both server-auth and owner-auth event bindings this
        // peer is authority for. Server-auth subscriptions land in _disposables (torn down
        // only at despawn); owner-auth in _ownerDisposables (torn down at ownership loss).
        // Call this ONCE at spawn — an ownership re-gain must use SubscribeOwnerEventBindings
        // instead, or the server-auth subs in _disposables get duplicated on every regain.
        private void SubscribeEventBindingsAsAuthority()
        {
            if (_system == null) return;

            for (int i = 0; i < _eventBindings.Length; i++)
            {
                var binding = _eventBindings[i];
                bool isAuthority = binding.Authority == AuthorityMode.Server ? IsServer : IsOwner;
                if (!isAuthority) continue;

                // Host-owner (IsServer && IsOwner) bypasses the owner->server hop and broadcasts
                // directly to NotServer. Pure client owner submits to server, which relays.
                bool isOwnerSubmit = binding.Authority == AuthorityMode.Owner && !IsServer;
                ref var bag = ref (binding.Authority == AuthorityMode.Owner ? ref _ownerDisposables : ref _disposables);
                binding.SubscribeAsAuthority(ref bag, (byte)i, _system, NetworkObjectId, isOwnerSubmit);
            }
        }

        // Ownership-transfer subscribe: wires ONLY owner-auth event bindings into
        // _ownerDisposables, which OnLostOwnership disposes. Server-auth event bindings are
        // deliberately skipped — they were subscribed once at spawn into _disposables and
        // stay live across ownership changes. Re-subscribing them here (as the old shared
        // SubscribeEventBindingsAsAuthority did) double-subscribed every server-auth Subject
        // on a host that lost then regained ownership, so each server event fired N+1 times.
        private void SubscribeOwnerEventBindings()
        {
            if (_system == null) return;

            for (int i = 0; i < _eventBindings.Length; i++)
            {
                var binding = _eventBindings[i];
                if (binding.Authority != AuthorityMode.Owner || !IsOwner) continue;

                // Host-owner broadcasts directly to NotServer; pure client owner submits to server.
                bool isOwnerSubmit = !IsServer;
                binding.SubscribeAsAuthority(ref _ownerDisposables, (byte)i, _system, NetworkObjectId, isOwnerSubmit);
            }
        }
    }
}
