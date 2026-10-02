using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Rubickanov.Save
{
    /// <summary>
    /// The player's data held in memory and gone with the store: for tests, and for a game that must leave no trace.
    /// It keeps copies, so what a caller does with its bytes afterwards changes nothing kept. Every write is a moment
    /// later than the one before, so a rewritten picture always reads as changed. Pictures decode on the main thread.
    /// </summary>
    public sealed class MemorySaveStore : ISaveStore
    {
        private static readonly Comparison<SaveEntry> ByName = (a, b) => string.CompareOrdinal(a.Key.Name, b.Key.Name);

        private readonly Dictionary<SaveKey, Kept> _kept = new();
        private readonly object _lock = new();
        private DateTime _clock = DateTime.UtcNow;

        public UniTask<byte[]> ReadAsync(SaveKey key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_lock)
            {
                return UniTask.FromResult(_kept.TryGetValue(key, out Kept kept) ? (byte[])kept.Bytes.Clone() : null);
            }
        }

        public UniTask<byte[]> ReadStartAsync(SaveKey key, int count, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_lock)
            {
                return UniTask.FromResult(_kept.TryGetValue(key, out Kept kept)
                    ? kept.Bytes.AsSpan(0, Math.Max(0, Math.Min(count, kept.Bytes.Length))).ToArray()
                    : null);
            }
        }

        public UniTask WriteAsync(SaveKey key, byte[] bytes, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var copy = (byte[])bytes.Clone();
            lock (_lock)
            {
                DateTime now = DateTime.UtcNow;
                _clock = now > _clock ? now : _clock.AddTicks(1);
                _kept[key] = new Kept(copy, _clock);
            }

            return UniTask.CompletedTask;
        }

        public UniTask<bool> DeleteAsync(SaveKey key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_lock)
            {
                return UniTask.FromResult(_kept.Remove(key));
            }
        }

        public UniTask<SaveEntry?> FindAsync(SaveKey key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_lock)
            {
                return UniTask.FromResult(_kept.TryGetValue(key, out Kept kept)
                    ? new SaveEntry(key, kept.Bytes.Length, kept.Written)
                    : (SaveEntry?)null);
            }
        }

        public UniTask<IReadOnlyList<SaveEntry>> ListAsync(SaveArea area, string prefix = "", CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            prefix ??= "";
            lock (_lock)
            {
                var entries = new List<SaveEntry>();
                foreach (KeyValuePair<SaveKey, Kept> pair in _kept)
                {
                    if (pair.Key.Area == area && pair.Key.Name.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        entries.Add(new SaveEntry(pair.Key, pair.Value.Bytes.Length, pair.Value.Written));
                    }
                }

                entries.Sort(ByName);
                return UniTask.FromResult<IReadOnlyList<SaveEntry>>(entries);
            }
        }

        public async UniTask<Texture2D> ReadPictureAsync(SaveKey key, CancellationToken ct = default)
        {
            byte[] bytes = await ReadAsync(key, ct);
            if (bytes == null)
            {
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
            if (!texture.LoadImage(bytes, true))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                return null;
            }

            texture.name = key.Leaf;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;
            return texture;
        }

        public string Describe(SaveKey key) => $"memory:{key}";

        private readonly struct Kept
        {
            public Kept(byte[] bytes, DateTime written)
            {
                Bytes = bytes;
                Written = written;
            }

            public byte[] Bytes { get; }

            public DateTime Written { get; }
        }
    }
}
