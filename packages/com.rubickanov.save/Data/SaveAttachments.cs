using System;
using System.Collections.Generic;

namespace Rubickanov.Save.Data
{
    /// <summary>
    /// A section's binary payloads kept as files beside its text (pictures, chunks of terrain), by name: what a capture
    /// attaches with <see cref="SaveWriter.Attach"/> and what a restore reads with <see cref="SaveReader.Attachment"/>.
    /// The bytes are kept as given, not copied: do not change them after attaching.
    /// </summary>
    public sealed class SaveAttachments
    {
        private readonly Dictionary<string, byte[]> _bytes = new(StringComparer.Ordinal);

        public int Count => _bytes.Count;

        /// <summary>Every payload by name.</summary>
        public IReadOnlyDictionary<string, byte[]> All => _bytes;

        /// <summary>Whether <paramref name="name"/> may name a section or a payload: <c>[a-z0-9-]</c>, not empty.</summary>
        public static bool IsPlain(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-'))
                {
                    return false;
                }
            }

            return true;
        }

        public void Set(string name, byte[] bytes)
        {
            if (!IsPlain(name))
            {
                throw new ArgumentException($"An attachment's name is [a-z0-9-]: \"{name}\".", nameof(name));
            }

            if (bytes == null)
            {
                _bytes.Remove(name);
                return;
            }

            _bytes[name] = bytes;
        }

        /// <summary>The payload named <paramref name="name"/>; null when there is none.</summary>
        public byte[] Get(string name) => name != null && _bytes.TryGetValue(name, out byte[] bytes) ? bytes : null;
    }
}
