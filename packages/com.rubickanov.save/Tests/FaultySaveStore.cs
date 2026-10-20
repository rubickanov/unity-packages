using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Rubickanov.Save.Tests
{
    /// <summary>
    /// A store over another that fails like a dying process: from the k-th write on every write and delete throws
    /// <see cref="SaveStoreException"/>. Each write can also sleep first, on the calling thread, as a slow disk does.
    /// </summary>
    internal sealed class FaultySaveStore : ISaveStore
    {
        private int _writes;
        private int _failFrom = int.MaxValue;

        public FaultySaveStore(ISaveStore inner) => Inner = inner;

        public ISaveStore Inner { get; }

        /// <summary>Writes asked so far, failed ones too.</summary>
        public int Writes => Volatile.Read(ref _writes);

        /// <summary>How long each write sleeps before it lands.</summary>
        public int DelayMs { get; set; }

        /// <summary>The <paramref name="k"/>-th write from now (1-based) and everything after it fail.</summary>
        public void FailFrom(int k) => _failFrom = Writes + k;

        public void Heal() => _failFrom = int.MaxValue;

        /// <summary>Flips the last byte of <paramref name="key"/> in the inner store, as bad sectors do.</summary>
        public void Corrupt(SaveKey key)
        {
            byte[] bytes = Inner.ReadAsync(key).GetAwaiter().GetResult();
            bytes[bytes.Length - 1] ^= 0x5A;
            Inner.WriteAsync(key, bytes).GetAwaiter().GetResult();
        }

        public UniTask<byte[]> ReadAsync(SaveKey key, CancellationToken ct = default) => Inner.ReadAsync(key, ct);

        public UniTask<byte[]> ReadStartAsync(SaveKey key, int count, CancellationToken ct = default) => Inner.ReadStartAsync(key, count, ct);

        public UniTask WriteAsync(SaveKey key, byte[] bytes, CancellationToken ct = default)
        {
            int n = Interlocked.Increment(ref _writes);
            if (DelayMs > 0)
            {
                Thread.Sleep(DelayMs);
            }

            if (n >= _failFrom)
            {
                throw new SaveStoreException($"write {n} of {key} failed", null);
            }

            return Inner.WriteAsync(key, bytes, ct);
        }

        public UniTask<bool> DeleteAsync(SaveKey key, CancellationToken ct = default)
        {
            if (Writes >= _failFrom)
            {
                throw new SaveStoreException($"delete of {key} failed", null);
            }

            return Inner.DeleteAsync(key, ct);
        }

        public UniTask<SaveEntry?> FindAsync(SaveKey key, CancellationToken ct = default) => Inner.FindAsync(key, ct);

        public UniTask<IReadOnlyList<SaveEntry>> ListAsync(SaveArea area, string prefix = "", CancellationToken ct = default) =>
            Inner.ListAsync(area, prefix, ct);

        public UniTask<Texture2D> ReadPictureAsync(SaveKey key, CancellationToken ct = default) => Inner.ReadPictureAsync(key, ct);

        public string Describe(SaveKey key) => Inner.Describe(key);
    }
}
