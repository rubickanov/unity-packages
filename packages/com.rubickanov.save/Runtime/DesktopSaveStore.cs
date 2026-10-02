using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Rubickanov.Save
{
    /// <summary>
    /// The player's data as files under one folder, the player's data folder in the game, laid out as each area says:
    /// an area in a folder of its own under it (<c>Replays/</c>), an area at the root as files beside the others
    /// (<c>settings.json</c>, <c>progress.json</c>), each seeing only its own names. A write goes to a <c>.tmp</c> file
    /// beside its own, then replaces it. The disk is quick, so every call works at once on the calling thread; whoever
    /// writes much, like a saved replay, calls it from the thread pool. Pictures decode off the main thread through
    /// Unity's own loader.
    /// </summary>
    public sealed class DesktopSaveStore : ISaveStore
    {
        /// <summary>What a file being written is called till it replaces its own; listings leave it out.</summary>
        public const string TempEnd = ".tmp";

        private static readonly Comparison<SaveEntry> ByName = (a, b) => string.CompareOrdinal(a.Key.Name, b.Key.Name);

        private readonly string _root;

        /// <param name="root">The folder that holds everything: the player's data folder, or a test's own.</param>
        public DesktopSaveStore(string root)
        {
            _root = root;
        }

        /// <summary>The store over the player's data folder.</summary>
        public static DesktopSaveStore ForPlayer() => new(Application.persistentDataPath);

        public UniTask<byte[]> ReadAsync(SaveKey key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            string path = PathOf(key);
            return UniTask.FromResult(Io(() => File.Exists(path) ? File.ReadAllBytes(path) : null));
        }

        public UniTask<byte[]> ReadStartAsync(SaveKey key, int count, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            string path = PathOf(key);
            return UniTask.FromResult(Io(() => File.Exists(path) ? ReadStart(path, count) : null));
        }

        public UniTask WriteAsync(SaveKey key, byte[] bytes, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            string path = PathOf(key);
            string temp = path + TempEnd;
            Io(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }

                return true;
            });
            return UniTask.CompletedTask;
        }

        public UniTask<bool> DeleteAsync(SaveKey key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            string path = PathOf(key);
            return UniTask.FromResult(Io(() =>
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                File.Delete(path);
                return true;
            }));
        }

        public UniTask<SaveEntry?> FindAsync(SaveKey key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            string path = PathOf(key);
            return UniTask.FromResult(Io(() =>
            {
                var file = new FileInfo(path);
                return file.Exists ? new SaveEntry(key, file.Length, file.LastWriteTimeUtc) : (SaveEntry?)null;
            }));
        }

        public UniTask<IReadOnlyList<SaveEntry>> ListAsync(SaveArea area, string prefix = "", CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            prefix ??= "";
            RequireArea(area);
            string areaPath = Path.Combine(_root, area.Folder);
            int slash = prefix.LastIndexOf('/');
            string under = Path.Combine(areaPath, slash >= 0 ? Local(prefix.Substring(0, slash)) : "");
            return UniTask.FromResult(Io<IReadOnlyList<SaveEntry>>(() =>
            {
                if (!Directory.Exists(under))
                {
                    return Array.Empty<SaveEntry>();
                }

                // Areas at the root are single files beside each other; the rest keep folders of any depth.
                SearchOption depth = area.Stem != null ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
                var entries = new List<SaveEntry>();
                foreach (string path in Directory.EnumerateFiles(under, "*", depth))
                {
                    string name = Path.GetRelativePath(areaPath, path).Replace(Path.DirectorySeparatorChar, '/');
                    if (name.StartsWith(prefix, StringComparison.Ordinal) && !name.EndsWith(TempEnd, StringComparison.Ordinal) &&
                        area.Owns(name))
                    {
                        var file = new FileInfo(path);
                        entries.Add(new SaveEntry(new SaveKey(area, name), file.Length, file.LastWriteTimeUtc));
                    }
                }

                entries.Sort(ByName);
                return entries;
            }));
        }

        public async UniTask<Texture2D> ReadPictureAsync(SaveKey key, CancellationToken ct = default)
        {
            string path = PathOf(key);
            DownloadedTextureParams parameters = DownloadedTextureParams.Default;
            parameters.readable = false;
            parameters.mipmapChain = true;
            parameters.linearColorSpace = false;
            using UnityWebRequest request = UnityWebRequestTexture.GetTexture(new Uri(path), parameters);
            try
            {
                await request.SendWebRequest().WithCancellation(ct);
            }
            catch (UnityWebRequestException)
            {
                return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                return null;
            }

            Texture2D texture = DownloadHandlerTexture.GetContent(request);
            if (texture == null)
            {
                return null;
            }

            texture.name = key.Leaf;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;
            return texture;
        }

        public string Describe(SaveKey key) => PathOf(key);

        private static void RequireArea(SaveArea area)
        {
            if (!area.IsValid)
            {
                throw new ArgumentException("No area", nameof(area));
            }
        }

        private static string Local(string name) => name.Replace('/', Path.DirectorySeparatorChar);

        private string PathOf(SaveKey key)
        {
            RequireArea(key.Area);
            return Path.Combine(_root, key.Area.Folder, Local(key.Name));
        }

        private static byte[] ReadStart(string path, int count)
        {
            using FileStream stream = File.OpenRead(path);
            var bytes = new byte[Math.Max(0, (int)Math.Min(count, stream.Length))];
            int read = 0;
            while (read < bytes.Length)
            {
                int got = stream.Read(bytes, read, bytes.Length - read);
                if (got <= 0)
                {
                    break;
                }

                read += got;
            }

            return read == bytes.Length ? bytes : bytes.AsSpan(0, read).ToArray();
        }

        private static T Io<T>(Func<T> work)
        {
            try
            {
                return work();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new SaveStoreException(e.Message, e);
            }
        }
    }
}
