using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode
{
    // State-application surface: reads FastBufferReader payloads produced by
    // EntityReplicationSystem and applies them to bindings. Lives on a partial so the
    // main file stays focused on lifecycle; all private state (_bindings, _maskByteCount,
    // _dirtyMaskBuffer, _ownerSubmitTickSync, …) is shared with the main partial.
    public partial class EntityReplicator
    {
        /// <summary>
        /// Apply incoming state from a FastBufferReader (named message path).
        /// The reader position is at serverTick (mask + fields follow). The
        /// <paramref name="serverTick"/> out parameter is surfaced so the caller
        /// (EntityReplicationSystem) can route it to the prediction reconcile
        /// hook without re-parsing the wire payload.
        /// </summary>
        internal unsafe void ApplyStateBuffer(FastBufferReader reader, StateApplyMode mode, out int serverTick)
        {
            reader.ReadValueSafe(out serverTick);
            var mask = stackalloc byte[_maskByteCount];
            reader.ReadBytesSafe(mask, _maskByteCount);
            double receivedTime = serverTick * _tickInterval;

            for (int i = 0; i < _bindings.Length; i++)
            {
                if ((mask[i >> 3] & (1 << (i & 7))) == 0) continue;

                // Server-auth fields always apply. Owner-auth fields apply or skip
                // depending on mode — the short-circuit on authority keeps server-auth
                // fields out of the decision entirely.
                bool skip = _bindingAuthorities[i] == AuthorityMode.Owner && mode switch
                {
                    StateApplyMode.SkipOwnerAuth => true,
                    StateApplyMode.SkipOwnerAuthIfLocallyWritten => _bindings[i].OwnerWroteSinceSpawn,
                    _ => false,
                };

                if (skip)
                {
                    _bindings[i].Skip(reader);
                    continue;
                }

                _bindings[i].ReadFrom(reader);
                _bindings[i].ApplyFromNetwork(receivedTime);
            }
        }

        /// <summary>
        /// Server-side: apply owner-submitted state, validate authority, re-mark dirty for relay.
        /// </summary>
        internal unsafe void ApplyOwnerSubmission(FastBufferReader reader, int senderTick)
        {
            int serverTick = NetworkManager.NetworkTickSystem.ServerTime.Tick;
            _ownerSubmitTickSync.Update(serverTick, senderTick);

            double receivedTime = (senderTick + _ownerSubmitTickSync.Offset) * _tickInterval;
            var mask = stackalloc byte[_maskByteCount];
            reader.ReadBytesSafe(mask, _maskByteCount);

            for (int i = 0; i < _bindings.Length; i++)
            {
                if ((mask[i >> 3] & (1 << (i & 7))) == 0) continue;

                if (_bindingAuthorities[i] != AuthorityMode.Owner)
                {
                    // Owner tried to write a server-auth field — reject but keep the reader aligned.
                    Debug.LogWarning($"[EntityReplicator] Owner submitted server-auth field index {i} on '{gameObject.name}'. Dropping.");
                    _bindings[i].Skip(reader);
                    continue;
                }

                _bindings[i].ReadFrom(reader);
                _bindings[i].ApplyFromNetwork(receivedTime);
                // Re-mark dirty so the next ServerTick relays to other clients.
                _bindings[i].MarkDirty();
            }
        }

        /// <summary>
        /// Build a full-snapshot payload for initial sync (server-side).
        /// Writes serverTick + full mask + all field values into the provided writer.
        /// </summary>
        internal unsafe void BuildInitialSyncPayload(FastBufferWriter writer)
        {
            if (_bindings.Length == 0) return;

            // Full mask: set every bit for every binding.
            for (int j = 0; j < _maskByteCount; j++) _dirtyMaskBuffer[j] = 0xFF;

            int serverTick = NetworkManager.NetworkTickSystem.ServerTime.Tick;
            writer.WriteValueSafe(serverTick);
            fixed (byte* maskPtr = _dirtyMaskBuffer)
                writer.WriteBytesSafe(maskPtr, _maskByteCount);

            for (int i = 0; i < _bindings.Length; i++)
                _bindings[i].WriteSnapshotTo(writer);
        }
    }
}
