using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Save
{
    /// <summary>Saved data as text: UTF-8 without a byte order mark, read in whatever Unicode its mark says.</summary>
    public static class SaveText
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        // UTF-32 LE before UTF-16 LE: its mark starts with UTF-16 LE's.
        private static readonly Encoding[] Marked =
        {
            new UTF32Encoding(false, true), Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode,
            new UTF32Encoding(true, true),
        };

        public static byte[] Encode(string text) => Utf8.GetBytes(text);

        public static string Decode(byte[] bytes)
        {
            foreach (Encoding encoding in Marked)
            {
                byte[] mark = encoding.GetPreamble();
                if (bytes.AsSpan().StartsWith(mark))
                {
                    return encoding.GetString(bytes, mark.Length, bytes.Length - mark.Length);
                }
            }

            return Utf8.GetString(bytes);
        }

        /// <summary><paramref name="key"/> as text; null when there is none.</summary>
        public static async UniTask<string> ReadTextAsync(this ISaveStore store, SaveKey key, CancellationToken ct = default)
        {
            byte[] bytes = await store.ReadAsync(key, ct);
            return bytes != null ? Decode(bytes) : null;
        }

        public static UniTask WriteTextAsync(this ISaveStore store, SaveKey key, string text, CancellationToken ct = default) =>
            store.WriteAsync(key, Encode(text), ct);
    }
}
