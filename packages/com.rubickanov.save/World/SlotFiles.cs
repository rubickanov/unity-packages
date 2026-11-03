using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Rubickanov.Save.Data;

namespace Rubickanov.Save.World
{
    /// <summary>A file a manifest names, with what it must be: its length and CRC-32.</summary>
    internal readonly struct FileRef
    {
        public FileRef(string file, long size, uint crc)
        {
            File = file;
            Size = size;
            Crc = crc;
        }

        public string File { get; }

        public long Size { get; }

        public uint Crc { get; }

        public bool Matches(byte[] bytes) => bytes != null && bytes.LongLength == Size && SlotFiles.Crc32(bytes) == Crc;

        public void WriteTo(SaveWriter w)
        {
            w.String("file", File);
            w.Long("size", Size);
            w.String("crc", SlotFiles.Hex(Crc));
        }
    }

    /// <summary>A section as a manifest lists it.</summary>
    internal sealed class SectionEntry
    {
        public string Id;
        public int Version;
        public FileRef File;
        public readonly List<KeyValuePair<string, FileRef>> Attachments = new();
    }

    /// <summary>Names of a slot's files, generation numbers, checksums and the manifest's own format.</summary>
    internal static class SlotFiles
    {
        public const string ManifestStem = "manifest";

        public static readonly UTF8Encoding Utf8 = new(false);

        private static readonly uint[] Table = MakeTable();

        public static string Manifest(int generation) => $"{ManifestStem}.{Number(generation)}.json";

        public static string Section(string id, int generation) => $"{id}.{Number(generation)}.json";

        public static string Attachment(string id, string name, int generation) => $"{id}.{name}.{Number(generation)}.bin";

        public static string Number(int generation) => generation.ToString("D6", CultureInfo.InvariantCulture);

        /// <summary><c>pieces.000043.json</c> → <c>pieces.000043.broken.json</c>.</summary>
        public static string BrokenCopy(string file)
        {
            int dot = file.LastIndexOf('.');
            return dot < 0 ? file + ".broken" : file.Substring(0, dot) + ".broken" + file.Substring(dot);
        }

        public static bool IsBrokenCopy(string file) => file.IndexOf(".broken.", StringComparison.Ordinal) >= 0;

        /// <summary>The generation in any slot file's name (copies of broken ones too); -1 when it has none.</summary>
        public static int Generation(string file)
        {
            string stem;
            if (file.EndsWith(".json", StringComparison.Ordinal))
            {
                stem = file.Substring(0, file.Length - 5);
            }
            else if (file.EndsWith(".bin", StringComparison.Ordinal))
            {
                stem = file.Substring(0, file.Length - 4);
            }
            else
            {
                return -1;
            }

            if (stem.EndsWith(".broken", StringComparison.Ordinal))
            {
                stem = stem.Substring(0, stem.Length - 7);
            }

            int dot = stem.LastIndexOf('.');
            return dot < 0 ? -1 : Digits(stem, dot + 1, stem.Length);
        }

        /// <summary>The generation of <c>manifest.&lt;N&gt;.json</c>; -1 for any other name.</summary>
        public static int ManifestGeneration(string file)
        {
            int start = ManifestStem.Length + 1;
            if (!file.StartsWith(ManifestStem + ".", StringComparison.Ordinal) || !file.EndsWith(".json", StringComparison.Ordinal))
            {
                return -1;
            }

            return Digits(file, start, file.Length - 5);
        }

        public static uint Crc32(byte[] bytes)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < bytes.Length; i++)
            {
                crc = Table[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);
            }

            return ~crc;
        }

        public static string Hex(uint crc) => crc.ToString("x8", CultureInfo.InvariantCulture);

        /// <summary>The sections a manifest lists, by id; false and why when the list is not what a slot writes.</summary>
        public static bool TryEntries(SaveNode manifest, out List<SectionEntry> entries, out string why)
        {
            entries = new List<SectionEntry>();
            why = null;
            SaveNode sections = manifest["sections"];
            if (sections == null || sections.IsNull)
            {
                return true;
            }

            if (!sections.IsObject)
            {
                why = "sections is not an object";
                return false;
            }

            IReadOnlyList<string> ids = sections.Keys;
            for (int i = 0; i < ids.Count; i++)
            {
                SaveNode e = sections[ids[i]];
                SaveNode version = e.IsObject ? e["version"] : null;
                if (!SaveAttachments.IsPlain(ids[i]) || !TryRef(e, out FileRef file) || version == null || !version.IsInteger ||
                    version.Long < 1 || version.Long > int.MaxValue)
                {
                    why = $"sections/{ids[i]} is not a section entry";
                    return false;
                }

                var entry = new SectionEntry { Id = ids[i], Version = (int)version.Long, File = file };
                SaveNode attachments = e["attachments"];
                if (attachments != null && !attachments.IsNull)
                {
                    if (!attachments.IsObject)
                    {
                        why = $"sections/{ids[i]}/attachments is not an object";
                        return false;
                    }

                    IReadOnlyList<string> names = attachments.Keys;
                    for (int k = 0; k < names.Count; k++)
                    {
                        if (!SaveAttachments.IsPlain(names[k]) || !TryRef(attachments[names[k]], out FileRef bin))
                        {
                            why = $"sections/{ids[i]}/attachments/{names[k]} is not a file entry";
                            return false;
                        }

                        entry.Attachments.Add(new KeyValuePair<string, FileRef>(names[k], bin));
                    }
                }

                entries.Add(entry);
            }

            return true;
        }

        /// <summary>Kind → version from a manifest's kinds; entries that are not plain versions are skipped.</summary>
        public static Dictionary<string, int> Kinds(SaveNode manifest)
        {
            var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
            SaveNode node = manifest?["kinds"];
            if (node == null || !node.IsObject)
            {
                return kinds;
            }

            IReadOnlyList<string> keys = node.Keys;
            for (int i = 0; i < keys.Count; i++)
            {
                SaveNode v = node[keys[i]];
                if (v.IsInteger && v.Long >= 0 && v.Long <= int.MaxValue)
                {
                    kinds[keys[i]] = (int)v.Long;
                }
            }

            return kinds;
        }

        private static bool TryRef(SaveNode e, out FileRef file)
        {
            file = default;
            if (e == null || !e.IsObject)
            {
                return false;
            }

            SaveNode name = e["file"];
            SaveNode size = e["size"];
            SaveNode crc = e["crc"];
            if (name == null || name.Kind != SaveNodeKind.String || !SaveKey.IsValidName(name.String) || name.String.IndexOf('/') >= 0 ||
                size == null || !size.IsInteger || size.Long < 0 || crc == null || crc.Kind != SaveNodeKind.String ||
                !uint.TryParse(crc.String, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value) ||
                crc.String.Length != 8)
            {
                return false;
            }

            file = new FileRef(name.String, size.Long, value);
            return true;
        }

        private static int Digits(string s, int start, int end)
        {
            int length = end - start;
            if (length < 1 || length > 9)
            {
                return -1;
            }

            int n = 0;
            for (int i = start; i < end; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9')
                {
                    return -1;
                }

                n = n * 10 + (c - '0');
            }

            return n;
        }

        private static uint[] MakeTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[i] = c;
            }

            return table;
        }
    }
}
