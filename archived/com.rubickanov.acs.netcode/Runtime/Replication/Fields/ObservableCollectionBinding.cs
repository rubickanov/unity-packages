using System.Collections.Generic;
using ObservableCollections;
using R3;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    // Op-codes shared by all collection bindings. Values are on the wire, so DO NOT
    // renumber — add new codes at the end. Each binding kind uses the subset that
    // applies to it (see below). A single flat namespace keeps the receiver dispatch
    // loop trivial and lets a future mixed-collection implementation reuse decoders.
    internal enum CollectionOpCode : byte
    {
        None = 0,
        InsertAt = 1,    // list: insert index, value
        RemoveAt = 2,    // list: remove at index (value not on wire — ObservableList only carries the index)
        Replace = 3,     // list: index, new value
        Clear = 4,       // list + dict + hashset + ring buffer: clear all (used in initial-sync snapshot and as a delta op when the source collection raises Reset)
        AddKey = 5,      // dict: key, value — new entry
        RemoveKey = 6,   // dict: key
        ReplaceKey = 7,  // dict: key, new value
        AddValue = 8,    // hashset: add a value; ring buffer: AddLast (wire payload identical — 1 byte opcode + codec.Write(value))
        RemoveValue = 9, // hashset: remove by value (ring buffer does not transmit removes — eviction is derived on the receiver with matching capacity)
    }

    /// <summary>
    /// Base for delta-replicated observable collections. Shares the length-prefix wire
    /// framing, dirty-flag bookkeeping, and Skip() implementation between collection
    /// kinds (list / dictionary / hashset / ring buffer) so each concrete binding only
    /// owns its op-queue and (de)serialisation.
    /// <para/>
    /// Wire framing per field: <c>ushort lengthBytes</c> + opCount-prefixed op list.
    /// The length prefix exists so <see cref="Skip"/> can advance the reader without
    /// knowing the codec layout — the scalar path has no such prefix (codec.Size is
    /// constant) and is deliberately not affected by this change.
    /// </summary>
    internal abstract class ObservableCollectionBinding : ReplicatedFieldBinding
    {
        // Wire-framing overhead per write: 2 bytes length prefix + 2 bytes op count.
        // Internal rather than protected so tests can reference it when asserting on
        // empty-delta payload size; derived collection bindings still have full access.
        internal const int HeaderBytes = sizeof(ushort) + sizeof(ushort);

        // Collections default to InterpolationMode.None — no lerp makes sense for
        // variable-sized state. IsInterpolated stays false (base default).

        public sealed override void Skip(FastBufferReader reader)
        {
            // Length prefix tells us exactly how far to advance; we don't need to know
            // the op layout. Safe for forward-compatible additions to opcodes.
            reader.ReadValueSafe(out ushort lengthBytes);
            reader.Seek(reader.Position + lengthBytes);
        }

        // Collections apply ops immediately inside ReadFrom — there is no meaningful
        // "buffer + apply later" path because collections do not interpolate. Leaving
        // ApplyFromNetwork as a no-op keeps the contract with EntityReplicator.
        public sealed override void ApplyFromNetwork(double receivedTime) { }
    }

    /// <summary>
    /// Delta replication for <see cref="ObservableList{T}"/>. Subscribes on the authority
    /// peer to <c>ObserveAdd</c> / <c>ObserveRemove</c> / <c>ObserveReplace</c> /
    /// <c>ObserveReset</c>, queues one <see cref="CollectionOpCode"/> per event, and
    /// drains the queue on each dirty tick. Initial-sync emits a full <c>Clear + Insert*</c>
    /// sequence derived from the current list contents.
    /// </summary>
    [Preserve]
    internal sealed class ObservableListBinding<T> : ObservableCollectionBinding
        where T : unmanaged
    {
        // Intentionally not using a struct with explicit layout — per-op footprint
        // varies (RemoveAt / Clear carry no value). Keeping a typed Op struct makes
        // Size computation and WriteTo trivial without a second allocation path.
        private struct Op
        {
            public CollectionOpCode Code;
            public int Index;
            public T Value;
        }

        private readonly ObservableList<T> _list;
        private readonly IFieldCodec<T> _codec;
        private readonly List<Op> _ops = new();

        // Receiver suppression guard — set while ReadFrom is applying ops so that, on a
        // host where the authority subscription also happens to be live, we don't re-queue
        // the same op we just applied. Strictly defensive: on a remote client the
        // authority subscription isn't attached, and on the server's own outgoing batch
        // there is no inbound ReadFrom, so today there's no real echo path. The flag
        // keeps us safe against future wiring changes (e.g. client-authoritative relays).
        private bool _applyingRemote;

        public ObservableListBinding(ObservableList<T> list, IFieldCodec<T> codec)
        {
            _list = list;
            _codec = codec;
        }

        public override int Size
        {
            get
            {
                // Dynamic: EntityReplicationSystem polls this every tick when the field
                // is dirty so it can reserve the right number of bytes in the StateBatch
                // payload. Scalars return a constant here; we return header + current
                // pending ops.
                int total = HeaderBytes;
                for (int i = 0; i < _ops.Count; i++)
                    total += OpWireSize(_ops[i].Code);
                return total;
            }
        }

        // Full snapshot = Clear + Insert per element. Used by BuildInitialSyncPayload.
        // Deliberately recomputed each call; snapshots fire once at spawn per late-joiner
        // so the cost is immaterial.
        public override int SnapshotSize
        {
            get
            {
                int total = HeaderBytes;
                total += OpWireSize(CollectionOpCode.Clear);
                int insertSize = OpWireSize(CollectionOpCode.InsertAt);
                total += insertSize * _list.Count;
                return total;
            }
        }

        private int OpWireSize(CollectionOpCode code)
        {
            // 1 byte opcode + op-specific payload.
            return code switch
            {
                CollectionOpCode.InsertAt => 1 + sizeof(int) + _codec.Size,
                CollectionOpCode.Replace  => 1 + sizeof(int) + _codec.Size,
                CollectionOpCode.RemoveAt => 1 + sizeof(int),
                CollectionOpCode.Clear    => 1,
                _ => 1,
            };
        }

        public override void SubscribeAsAuthority(ref DisposableBag disposables)
        {
            // ObserveAdd fires on Add(value) (index = count-1) and on Insert(i, v). Both
            // map to InsertAt(index, value) on the wire — the receiver applies via
            // _list.Insert(index, value) which is index-correct in either case.
            _list.ObserveAdd().Subscribe(e =>
            {
                if (_applyingRemote) return;
                _ops.Add(new Op { Code = CollectionOpCode.InsertAt, Index = e.Index, Value = e.Value });
                MarkDirty();
            }).AddTo(ref disposables);

            _list.ObserveRemove().Subscribe(e =>
            {
                if (_applyingRemote) return;
                _ops.Add(new Op { Code = CollectionOpCode.RemoveAt, Index = e.Index });
                MarkDirty();
            }).AddTo(ref disposables);

            _list.ObserveReplace().Subscribe(e =>
            {
                if (_applyingRemote) return;
                _ops.Add(new Op { Code = CollectionOpCode.Replace, Index = e.Index, Value = e.NewValue });
                MarkDirty();
            }).AddTo(ref disposables);

            // ObservableList.Clear() raises ObserveReset once and does NOT emit per-element
            // ObserveRemove events (Cysharp contract), so a single Clear op reproduces the
            // observed authority-side state on the receiver.
            _list.ObserveReset().Subscribe(_ =>
            {
                if (_applyingRemote) return;
                _ops.Add(new Op { Code = CollectionOpCode.Clear });
                MarkDirty();
            }).AddTo(ref disposables);

            // ReactiveProperty.Subscribe replays the current value on subscribe; the
            // collection Observe* APIs do NOT — they are pure event streams. So unlike
            // scalar bindings we do NOT call ResetOwnerWroteSinceSpawn here; the flag
            // stays false until a genuine mutation fires an event.
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
            // Two-pass: reserve length prefix, write ops, then patch prefix. Avoids a
            // secondary scratch buffer and keeps the write path allocation-free after
            // the initial _ops list grow.
            int prefixPos = writer.Position;
            writer.WriteValueSafe((ushort)0);
            int contentStart = writer.Position;

            // Op count placeholder (patched below). Kept as ushort so a full-collection
            // snapshot of up to ~65k elements fits without overflow; an overflowing
            // collection is flagged below rather than producing a truncated payload.
            int opCountPos = writer.Position;
            writer.WriteValueSafe((ushort)0);

            int opsWritten = 0;

            if (includeSnapshot)
            {
                WriteOp(writer, new Op { Code = CollectionOpCode.Clear });
                opsWritten++;
                // Iterate via index to avoid allocating an enumerator. ObservableList<T>
                // exposes [] indexing; Count is O(1).
                for (int i = 0; i < _list.Count; i++)
                {
                    WriteOp(writer, new Op { Code = CollectionOpCode.InsertAt, Index = i, Value = _list[i] });
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
                Debug.LogError($"[ObservableListBinding<{typeof(T).Name}>] Payload {contentBytes}B exceeds ushort length prefix limit. Collection too large for a single StateBatch field — consider chunking.");
            }
            if (opsWritten > ushort.MaxValue)
            {
                Debug.LogError($"[ObservableListBinding<{typeof(T).Name}>] opCount {opsWritten} exceeds ushort limit.");
            }

            // Patch the length and op-count prefixes.
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
                case CollectionOpCode.InsertAt:
                case CollectionOpCode.Replace:
                    writer.WriteValueSafe(op.Index);
                    _codec.Write(writer, op.Value);
                    break;
                case CollectionOpCode.RemoveAt:
                    writer.WriteValueSafe(op.Index);
                    break;
                case CollectionOpCode.Clear:
                    break;
            }
        }

        public override void ReadFrom(FastBufferReader reader)
        {
            reader.ReadValueSafe(out ushort _);       // lengthBytes — content is self-delimiting via opCount, so it's read only to advance past.
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
                        case CollectionOpCode.InsertAt:
                        {
                            reader.ReadValueSafe(out int index);
                            var value = _codec.Read(reader);
                            // Clamp defensively: a mis-ordered batch could try to insert
                            // past the current Count (e.g. server added at index 3 before
                            // a Clear reached us). Clamping to [0, Count] keeps the list
                            // coherent with the stream of mutations the authority will
                            // send in its next tick rather than throwing.
                            int clamped = Mathf.Clamp(index, 0, _list.Count);
                            _list.Insert(clamped, value);
                            break;
                        }
                        case CollectionOpCode.Replace:
                        {
                            reader.ReadValueSafe(out int index);
                            var value = _codec.Read(reader);
                            if ((uint)index < (uint)_list.Count)
                                _list[index] = value;
                            else
                                Debug.LogWarning($"[ObservableListBinding<{typeof(T).Name}>] Replace at out-of-range index {index} (count {_list.Count}). Dropping op.");
                            break;
                        }
                        case CollectionOpCode.RemoveAt:
                        {
                            reader.ReadValueSafe(out int index);
                            if ((uint)index < (uint)_list.Count)
                                _list.RemoveAt(index);
                            else
                                Debug.LogWarning($"[ObservableListBinding<{typeof(T).Name}>] RemoveAt out-of-range index {index} (count {_list.Count}). Dropping op.");
                            break;
                        }
                        case CollectionOpCode.Clear:
                            _list.Clear();
                            break;
                        default:
                            // Forward-compat: unknown opcode means the payload layout is
                            // unknown and we cannot safely skip further ops. Best we can
                            // do is log and bail out; Skip() already handles the
                            // unknown-entity case via the length prefix.
                            Debug.LogError($"[ObservableListBinding<{typeof(T).Name}>] Unknown opcode {rawCode}; aborting read.");
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
            // WriteTo already drained the op queue; flip the authority-visible flag
            // back off so the next tick doesn't re-mark this binding dirty on an
            // empty queue. Uses the protected setter exposed by the base class.
            IsDirty = false;
        }

        public override void OnDespawn()
        {
            _ops.Clear();
        }
    }
}
