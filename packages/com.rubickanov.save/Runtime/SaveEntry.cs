using System;

namespace Rubickanov.Save
{
    /// <summary>A piece of saved data as a listing gives it: its key, its size and when it was last written.</summary>
    public readonly struct SaveEntry
    {
        public SaveEntry(SaveKey key, long size, DateTime written)
        {
            Key = key;
            Size = size;
            Written = written;
        }

        public SaveKey Key { get; }

        /// <summary>Its size in bytes.</summary>
        public long Size { get; }

        /// <summary>When it was last written, in UTC: a picture changed since it was decoded is decoded again.</summary>
        public DateTime Written { get; }

        public override string ToString() => $"{Key} ({Size} bytes)";
    }
}
