using System;
using System.Collections.Generic;
using System.Text;
using ObservableCollections;
using R3;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    // Dictionary key serialisation lives outside IFieldCodec<T> because that interface
    // is constrained to `T : unmanaged` — strings are the primary dictionary-key use
    // case (CooldownsAspect-style fields). Keeping this interface LOCAL to the dict
    // binding (no CodecRegistry registration) preserves the registry's unmanaged-only
    // invariant the scalar path relies on.
    internal interface IObservableDictionaryKeyCodec<TKey>
    {
        // Size of a single encoded key, in bytes. Dynamic for variable-length keys
        // (string); constant for unmanaged keys (delegates to IFieldCodec<T>.Size).
        int SizeOf(in TKey value);
        void Write(FastBufferWriter writer, in TKey value);
        TKey Read(FastBufferReader reader);
    }

    // String key codec. Wire format: ushort utf8ByteLen + utf8 bytes. Null is encoded
    // as empty string so receivers always get a non-null key. byteLen > ushort.MaxValue
    // is clamped to an empty write with a LogError — consistent with the ushort-overflow
    // guard in ObservableListBinding.WriteFramed.
    [Preserve]
    internal sealed class StringKeyCodec : IObservableDictionaryKeyCodec<string>
    {
        public static readonly StringKeyCodec Instance = new StringKeyCodec();

        public int SizeOf(in string value)
        {
            var s = value ?? string.Empty;
            return sizeof(ushort) + Encoding.UTF8.GetByteCount(s);
        }

        // UTF-8 keys shorter than this (in bytes) encode into a stack buffer — no heap alloc on
        // the common path. The byte[] that Encoding.UTF8.GetBytes(string) returns is otherwise
        // garbage on every replicated dictionary mutation.
        private const int StackEncodeCap = 256;

        public unsafe void Write(FastBufferWriter writer, in string value)
        {
            var s = value ?? string.Empty;
            int byteCount = Encoding.UTF8.GetByteCount(s);
            if (byteCount > ushort.MaxValue)
            {
                Debug.LogError($"[StringKeyCodec] UTF-8 byte length {byteCount} exceeds ushort limit; writing empty key. Source string (truncated): '{s.Substring(0, Mathf.Min(32, s.Length))}…'");
                writer.WriteValueSafe((ushort)0);
                return;
            }
            writer.WriteValueSafe((ushort)byteCount);
            if (byteCount == 0) return;

            Span<byte> buffer = byteCount <= StackEncodeCap ? stackalloc byte[StackEncodeCap] : new byte[byteCount];
            int written = Encoding.UTF8.GetBytes(s, buffer);
            fixed (byte* ptr = buffer)
            {
                writer.WriteBytesSafe(ptr, written);
            }
        }

        public unsafe string Read(FastBufferReader reader)
        {
            reader.ReadValueSafe(out ushort byteLen);
            if (byteLen == 0) return string.Empty;
            var bytes = new byte[byteLen];
            fixed (byte* ptr = bytes)
            {
                reader.ReadBytesSafe(ptr, byteLen);
            }
            return Encoding.UTF8.GetString(bytes);
        }
    }

    // Adapter for unmanaged keys: thin wrapper over IFieldCodec<T>. Constructed by
    // ReplicatedFieldBindingFactory.CreateObservableDictionary via ConstructorInfo.Invoke
    // (IL2CPP-safe) — AotHints preserves the common closed generics.
    [Preserve]
    internal sealed class UnmanagedKeyCodec<TKey> : IObservableDictionaryKeyCodec<TKey>
        where TKey : unmanaged
    {
        private readonly IFieldCodec<TKey> _inner;

        public UnmanagedKeyCodec(IFieldCodec<TKey> inner)
        {
            _inner = inner;
        }

        public int SizeOf(in TKey value) => _inner.Size;
        public void Write(FastBufferWriter writer, in TKey value) => _inner.Write(writer, value);
        public TKey Read(FastBufferReader reader) => _inner.Read(reader);
    }

    /// <summary>
    /// Delta replication for <see cref="ObservableDictionary{TKey,TValue}"/>. Mirrors
    /// <see cref="ObservableListBinding{T}"/>: subscribes to R3's Observe* on the authority,
    /// queues one <see cref="CollectionOpCode"/> per event, drains on each dirty tick.
    /// Initial-sync emits a <c>Clear + AddKey*</c> sequence derived from current entries.
    /// <para/>
    /// TKey has no unmanaged constraint — strings are supported via
    /// <see cref="StringKeyCodec"/>. TValue stays unmanaged (or <c>EntityRef</c>) so the
    /// value path reuses the scalar codec infrastructure unchanged.
    /// </summary>
    [Preserve]
    internal sealed class ObservableDictionaryBinding<TKey, TValue> : ObservableCollectionBinding
        where TKey : notnull
        where TValue : unmanaged
    {
        // Unlike the list Op, Key is a typed field here (not an index) — it holds the
        // actual TKey used for dispatch on the receiver. For reference-type TKey
        // (string) the struct stores a reference; no boxing.
        private struct Op
        {
            public CollectionOpCode Code;
            public TKey Key;
            public TValue Value;
        }

        private readonly ObservableDictionary<TKey, TValue> _dict;
        private readonly IObservableDictionaryKeyCodec<TKey> _keyCodec;
        private readonly IFieldCodec<TValue> _valueCodec;
        private readonly List<Op> _ops = new();

        // Receiver suppression guard — parallel to ObservableListBinding's _applyingRemote.
        // Guards against echo on host scenarios where the authority subscription also runs.
        private bool _applyingRemote;

        public ObservableDictionaryBinding(
            ObservableDictionary<TKey, TValue> dict,
            IObservableDictionaryKeyCodec<TKey> keyCodec,
            IFieldCodec<TValue> valueCodec)
        {
            _dict = dict;
            _keyCodec = keyCodec;
            _valueCodec = valueCodec;
        }

        public override int Size
        {
            get
            {
                // Dynamic, same contract as ObservableListBinding.Size. Per-op size
                // depends on the op's Key (variable for string) so iterate ops directly.
                int total = HeaderBytes;
                for (int i = 0; i < _ops.Count; i++)
                    total += OpWireSize(_ops[i]);
                return total;
            }
        }

        public override int SnapshotSize
        {
            get
            {
                // Snapshot = Clear + AddKey per current entry. Iterate dictionary to
                // sum the per-key dynamic sizes (strings vary).
                int total = HeaderBytes;
                total += 1; // Clear opcode only
                foreach (var kv in _dict)
                    total += 1 + _keyCodec.SizeOf(kv.Key) + _valueCodec.Size;
                return total;
            }
        }

        private int OpWireSize(in Op op)
        {
            // 1 byte opcode + op-specific payload.
            switch (op.Code)
            {
                case CollectionOpCode.AddKey:
                case CollectionOpCode.ReplaceKey:
                    return 1 + _keyCodec.SizeOf(op.Key) + _valueCodec.Size;
                case CollectionOpCode.RemoveKey:
                    return 1 + _keyCodec.SizeOf(op.Key);
                case CollectionOpCode.Clear:
                    return 1;
                default:
                    return 1;
            }
        }

        public override void SubscribeAsAuthority(ref DisposableBag disposables)
        {
            // ObservableDictionary's Observe* events carry KeyValuePair<TKey,TValue> as
            // their `Value` payload. e.Value.Key / e.Value.Value extract the pair.
            _dict.ObserveAdd().Subscribe(e =>
            {
                if (_applyingRemote) return;
                _ops.Add(new Op { Code = CollectionOpCode.AddKey, Key = e.Value.Key, Value = e.Value.Value });
                MarkDirty();
            }).AddTo(ref disposables);

            _dict.ObserveRemove().Subscribe(e =>
            {
                if (_applyingRemote) return;
                _ops.Add(new Op { Code = CollectionOpCode.RemoveKey, Key = e.Value.Key });
                MarkDirty();
            }).AddTo(ref disposables);

            _dict.ObserveReplace().Subscribe(e =>
            {
                if (_applyingRemote) return;
                _ops.Add(new Op { Code = CollectionOpCode.ReplaceKey, Key = e.NewValue.Key, Value = e.NewValue.Value });
                MarkDirty();
            }).AddTo(ref disposables);

            // Clear() on ObservableDictionary raises ObserveReset — subscribe so the
            // receiver sees a single Clear op (and does not have to guess from a run
            // of RemoveKey events).
            _dict.ObserveReset().Subscribe(_ =>
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
                foreach (var kv in _dict)
                {
                    WriteOp(writer, new Op { Code = CollectionOpCode.AddKey, Key = kv.Key, Value = kv.Value });
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
                Debug.LogError($"[ObservableDictionaryBinding<{typeof(TKey).Name},{typeof(TValue).Name}>] Payload {contentBytes}B exceeds ushort length prefix limit. Dictionary too large for a single StateBatch field — consider chunking.");
            }
            if (opsWritten > ushort.MaxValue)
            {
                Debug.LogError($"[ObservableDictionaryBinding<{typeof(TKey).Name},{typeof(TValue).Name}>] opCount {opsWritten} exceeds ushort limit.");
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
                case CollectionOpCode.AddKey:
                case CollectionOpCode.ReplaceKey:
                    _keyCodec.Write(writer, op.Key);
                    _valueCodec.Write(writer, op.Value);
                    break;
                case CollectionOpCode.RemoveKey:
                    _keyCodec.Write(writer, op.Key);
                    break;
                case CollectionOpCode.Clear:
                    break;
            }
        }

        public override void ReadFrom(FastBufferReader reader)
        {
            reader.ReadValueSafe(out ushort _);          // lengthBytes — used only by Skip
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
                        case CollectionOpCode.AddKey:
                        {
                            var key = _keyCodec.Read(reader);
                            var value = _valueCodec.Read(reader);
                            // Indexer assignment handles both the normal add and the defensive
                            // already-present case (mid-stream reorderings) in one hash, emitting
                            // ObserveAdd or ObserveReplace exactly as the old ContainsKey + Add/
                            // indexer pair did. The add-vs-replace diagnostic is dev-only.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                            if (_dict.ContainsKey(key))
                                Debug.LogWarning($"[ObservableDictionaryBinding<{typeof(TKey).Name},{typeof(TValue).Name}>] AddKey for already-present key; applying as Replace.");
#endif
                            _dict[key] = value;
                            break;
                        }
                        case CollectionOpCode.ReplaceKey:
                        {
                            var key = _keyCodec.Read(reader);
                            var value = _valueCodec.Read(reader);
                            // Same single-hash path: the indexer adds when missing (defensive)
                            // and replaces when present, emitting the matching observe event.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                            if (!_dict.ContainsKey(key))
                                Debug.LogWarning($"[ObservableDictionaryBinding<{typeof(TKey).Name},{typeof(TValue).Name}>] ReplaceKey for missing key; applying as Add.");
#endif
                            _dict[key] = value;
                            break;
                        }
                        case CollectionOpCode.RemoveKey:
                        {
                            var key = _keyCodec.Read(reader);
                            if (!_dict.Remove(key))
                                Debug.LogWarning($"[ObservableDictionaryBinding<{typeof(TKey).Name},{typeof(TValue).Name}>] RemoveKey for missing key. Dropping op.");
                            break;
                        }
                        case CollectionOpCode.Clear:
                            _dict.Clear();
                            break;
                        default:
                            // Forward-compat guard — same behaviour as the list binding.
                            Debug.LogError($"[ObservableDictionaryBinding<{typeof(TKey).Name},{typeof(TValue).Name}>] Unknown opcode {rawCode}; aborting read.");
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
