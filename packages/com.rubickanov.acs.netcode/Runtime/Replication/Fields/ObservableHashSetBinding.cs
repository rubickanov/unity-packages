using System.Collections.Generic;
using ObservableCollections;
using R3;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Delta replication for <see cref="ObservableHashSet{T}"/>. Mirrors
    /// <see cref="ObservableListBinding{T}"/>: subscribes on the authority peer to
    /// R3's Observe* events, queues one <see cref="CollectionOpCode"/> per event, and
    /// drains on each dirty tick. Initial-sync emits a <c>Clear + AddValue*</c> sequence
    /// derived from current entries.
    /// <para/>
    /// HashSet does not raise ObserveReplace — element identity IS the key. The wire
    /// uses AddValue / RemoveValue / Clear; there is no replace op on this path.
    /// </summary>
    [Preserve]
    internal sealed class ObservableHashSetBinding<T> : ObservableCollectionBinding
        where T : unmanaged
    {
        // Per-op struct — no index field (hashset is unordered). Kept as a struct so
        // Size and WriteFramed iterate without an allocating enumerator.
        private struct Op
        {
            public CollectionOpCode Code;
            public T Value;
        }

        private readonly ObservableHashSet<T> _set;
        private readonly IFieldCodec<T> _codec;
        private readonly List<Op> _ops = new();

        // Receiver suppression guard — parallel to ObservableListBinding._applyingRemote.
        // Guards against echo on host scenarios where the authority subscription also
        // runs while ReadFrom applies ops.
        private bool _applyingRemote;

        public ObservableHashSetBinding(ObservableHashSet<T> set, IFieldCodec<T> codec)
        {
            _set = set;
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
                total += addSize * _set.Count;
                return total;
            }
        }

        private int OpWireSize(CollectionOpCode code)
        {
            // 1 byte opcode + op-specific payload.
            return code switch
            {
                CollectionOpCode.AddValue    => 1 + _codec.Size,
                CollectionOpCode.RemoveValue => 1 + _codec.Size,
                CollectionOpCode.Clear       => 1,
                _ => 1,
            };
        }

        public override void SubscribeAsAuthority(ref DisposableBag disposables)
        {
            // ObservableHashSet.Add(v) raises ObserveAdd only for NEW elements (Cysharp
            // returns bool from Add and does not fire the event for duplicates), so we
            // don't need a contains-check before enqueueing.
            _set.ObserveAdd().Subscribe(e =>
            {
                if (_applyingRemote) return;
                _ops.Add(new Op { Code = CollectionOpCode.AddValue, Value = e.Value });
                MarkDirty();
            }).AddTo(ref disposables);

            _set.ObserveRemove().Subscribe(e =>
            {
                if (_applyingRemote) return;
                _ops.Add(new Op { Code = CollectionOpCode.RemoveValue, Value = e.Value });
                MarkDirty();
            }).AddTo(ref disposables);

            // HashSet.Clear() raises ObserveReset once and does NOT emit per-element
            // ObserveRemove events (Cysharp contract), so a single Clear op reproduces
            // the observed authority-side state on the receiver.
            _set.ObserveReset().Subscribe(_ =>
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
            // Two-pass: reserve length prefix + opCount, write ops, then patch. Same
            // shape as ObservableListBinding.WriteFramed — the framing belongs to
            // ObservableCollectionBinding but the per-op payload differs.
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
                foreach (var value in _set)
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
                Debug.LogError($"[ObservableHashSetBinding<{typeof(T).Name}>] Payload {contentBytes}B exceeds ushort length prefix limit. HashSet too large for a single StateBatch field — consider chunking.");
            }
            if (opsWritten > ushort.MaxValue)
            {
                Debug.LogError($"[ObservableHashSetBinding<{typeof(T).Name}>] opCount {opsWritten} exceeds ushort limit.");
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
                case CollectionOpCode.RemoveValue:
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
                            // HashSet.Add silent-drops duplicates (returns false without
                            // firing ObserveAdd). No warn: duplicate adds during
                            // snapshot+delta interleaving are a valid steady-state.
                            _set.Add(value);
                            break;
                        }
                        case CollectionOpCode.RemoveValue:
                        {
                            var value = _codec.Read(reader);
                            if (!_set.Remove(value))
                                Debug.LogWarning($"[ObservableHashSetBinding<{typeof(T).Name}>] RemoveValue for missing value. Dropping op.");
                            break;
                        }
                        case CollectionOpCode.Clear:
                            _set.Clear();
                            break;
                        default:
                            // Forward-compat guard — unknown opcode means unknown payload
                            // layout, same behaviour as ObservableListBinding.
                            Debug.LogError($"[ObservableHashSetBinding<{typeof(T).Name}>] Unknown opcode {rawCode}; aborting read.");
                            return;
                    }
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
