using System.Collections.Generic;
using ObservableCollections;
using R3;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Delta replication for <see cref="ObservableFixedSizeRingBuffer{T}"/>. Only
    /// <c>AddLast</c> transits the wire: the receiver's own fixed-size buffer is
    /// configured with the same capacity as the authority's (ACS aspect ctors run
    /// on both peers), so its <c>AddLast</c> auto-evicts the oldest element in
    /// lockstep with the authority. Eviction is therefore derived, not transmitted.
    /// <para/>
    /// Initial-sync emits a <c>Clear + AddValue*</c> sequence covering the authority's
    /// current contents (oldest → newest per <c>IReadOnlyCollection&lt;T&gt;</c>
    /// enumeration).
    /// <para/>
    /// Plain unbounded <c>ObservableRingBuffer&lt;T&gt;</c> is intentionally not
    /// supported — the scanner rejects it separately. This binding is for the
    /// fixed-size variant only.
    /// </summary>
    [Preserve]
    internal sealed class ObservableRingBufferBinding<T> : ObservableCollectionBinding
        where T : unmanaged
    {
        private struct Op
        {
            public CollectionOpCode Code;
            public T Value;
        }

        private readonly ObservableFixedSizeRingBuffer<T> _buffer;
        private readonly IFieldCodec<T> _codec;
        private readonly List<Op> _ops = new();

        // Receiver suppression guard — parallel to ObservableListBinding._applyingRemote.
        private bool _applyingRemote;

        public ObservableRingBufferBinding(ObservableFixedSizeRingBuffer<T> buffer, IFieldCodec<T> codec)
        {
            _buffer = buffer;
            _codec = codec;
        }

        public override int Size
        {
            get
            {
                int total = HeaderBytes;
                for (int i = 0; i < _ops.Count; i++)
                    total += OpWireSize(_ops[i].Code);
                return total;
            }
        }

        public override int SnapshotSize
        {
            get
            {
                int total = HeaderBytes;
                total += OpWireSize(CollectionOpCode.Clear);
                int addSize = OpWireSize(CollectionOpCode.AddValue);
                total += addSize * _buffer.Count;
                return total;
            }
        }

        private int OpWireSize(CollectionOpCode code)
        {
            return code switch
            {
                CollectionOpCode.AddValue => 1 + _codec.Size,
                CollectionOpCode.Clear    => 1,
                _ => 1,
            };
        }

        public override void SubscribeAsAuthority(ref DisposableBag disposables)
        {
            // Ring buffer authority transmits only AddLast. We deliberately ignore
            // ObserveRemove (fires on auto-eviction triggered by our own AddLast —
            // the receiver derives the same eviction locally) and ObserveReplace
            // (not a natural mutation on a ring buffer).
            //
            // ObserveReset fires on ObservableFixedSizeRingBuffer.Clear(), so we
            // subscribe to it and emit a Clear op — matches the list / dict / hashset
            // pattern.
            _buffer.ObserveAdd().Subscribe(e =>
            {
                if (_applyingRemote) return;
                _ops.Add(new Op { Code = CollectionOpCode.AddValue, Value = e.Value });
                MarkDirty();
            }).AddTo(ref disposables);

            _buffer.ObserveReset().Subscribe(_ =>
            {
                if (_applyingRemote) return;
                _ops.Add(new Op { Code = CollectionOpCode.Clear });
                MarkDirty();
            }).AddTo(ref disposables);
        }

        public override void WriteTo(FastBufferWriter writer)
        {
            WriteFramed(writer, drainOps: true, includeSnapshot: false);
        }

        public override void WriteSnapshotTo(FastBufferWriter writer)
        {
            WriteFramed(writer, drainOps: true, includeSnapshot: true);
        }

        private void WriteFramed(FastBufferWriter writer, bool drainOps, bool includeSnapshot)
        {
            int prefixPos = writer.Position;
            writer.WriteValueSafe((ushort)0);
            int contentStart = writer.Position;

            int opCountPos = writer.Position;
            writer.WriteValueSafe((ushort)0);

            int opsWritten = 0;

            if (includeSnapshot)
            {
                WriteOp(writer, new Op { Code = CollectionOpCode.Clear });
                opsWritten++;
                // ObservableFixedSizeRingBuffer enumerates oldest → newest. Matching
                // AddLast on the receiver reproduces the same order byte-for-byte.
                foreach (var value in _buffer)
                {
                    WriteOp(writer, new Op { Code = CollectionOpCode.AddValue, Value = value });
                    opsWritten++;
                }
            }
            else
            {
                for (int i = 0; i < _ops.Count; i++)
                {
                    WriteOp(writer, _ops[i]);
                    opsWritten++;
                }
            }

            if (drainOps)
                _ops.Clear();

            int endPos = writer.Position;
            int contentBytes = endPos - contentStart;
            if (contentBytes > ushort.MaxValue)
            {
                Debug.LogError($"[ObservableRingBufferBinding<{typeof(T).Name}>] Payload {contentBytes}B exceeds ushort length prefix limit. Ring buffer too large for a single StateBatch field — consider reducing capacity.");
            }
            if (opsWritten > ushort.MaxValue)
            {
                Debug.LogError($"[ObservableRingBufferBinding<{typeof(T).Name}>] opCount {opsWritten} exceeds ushort limit.");
            }

            writer.Seek(prefixPos);
            writer.WriteValueSafe((ushort)contentBytes);
            writer.Seek(opCountPos);
            writer.WriteValueSafe((ushort)opsWritten);
            writer.Seek(endPos);
        }

        private void WriteOp(FastBufferWriter writer, in Op op)
        {
            writer.WriteValueSafe((byte)op.Code);
            switch (op.Code)
            {
                case CollectionOpCode.AddValue:
                    _codec.Write(writer, op.Value);
                    break;
                case CollectionOpCode.Clear:
                    break;
            }
        }

        public override void ReadFrom(FastBufferReader reader)
        {
            reader.ReadValueSafe(out ushort _);       // lengthBytes — used only by Skip
            reader.ReadValueSafe(out ushort opCount);

            // Track snapshot AddLast count so we can diagnose capacity mismatch
            // (authority > receiver). Only meaningful when the payload starts with a
            // Clear — a pure-delta stream on a steady-state pair causes ordinary
            // eviction and is NOT a mismatch signal. We therefore only compare after
            // a Clear-prefixed run.
            bool sawLeadingClear = false;
            int addLastCount = 0;

            _applyingRemote = true;
            try
            {
                for (int i = 0; i < opCount; i++)
                {
                    reader.ReadValueSafe(out byte rawCode);
                    var code = (CollectionOpCode)rawCode;
                    switch (code)
                    {
                        case CollectionOpCode.AddValue:
                        {
                            var value = _codec.Read(reader);
                            _buffer.AddLast(value);
                            if (sawLeadingClear) addLastCount++;
                            break;
                        }
                        case CollectionOpCode.Clear:
                            _buffer.Clear();
                            // Only the FIRST op being Clear marks this as a snapshot
                            // payload for the capacity diagnostic. A Clear later in
                            // the stream resets the count but is still treated as a
                            // snapshot-like window (sender explicitly wiped state).
                            if (i == 0) sawLeadingClear = true;
                            addLastCount = 0;
                            break;
                        default:
                            // Forward-compat: unknown opcode means unknown payload
                            // layout. Same bail as list / hashset / dict.
                            Debug.LogError($"[ObservableRingBufferBinding<{typeof(T).Name}>] Unknown opcode {rawCode}; aborting read.");
                            return;
                    }
                }

                // Capacity-mismatch diagnostic. After a snapshot (Clear + N AddLast),
                // if the receiver's buffer ended up with fewer than N entries, it
                // auto-evicted during the snapshot replay — i.e. its Capacity is
                // smaller than the authority's. ACS assumes symmetric aspect
                // construction, so this is a user wiring bug worth surfacing.
                if (sawLeadingClear && addLastCount > _buffer.Count)
                {
                    Debug.LogError($"[ObservableRingBufferBinding<{typeof(T).Name}>] Snapshot produced receiver-side eviction ({addLastCount} AddLast ops → Count {_buffer.Count}, Capacity {_buffer.Capacity}). Capacity mismatch between peers — aspect ctor must configure identical capacity on both sides.");
                }
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        public override void ClearDirty()
        {
            IsDirty = false;
        }

        public override void OnDespawn()
        {
            _ops.Clear();
        }
    }
}
