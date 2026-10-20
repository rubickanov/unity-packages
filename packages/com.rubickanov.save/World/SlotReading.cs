using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Rubickanov.Save.Data;

namespace Rubickanov.Save.World
{
    /// <summary>A section this build knows, with the format its file is read and written in.</summary>
    internal sealed class KnownSection
    {
        public KnownSection(ISaveSection section)
        {
            Section = section;
            Format = new SaveFormat(section.Id, section.Migrations);
        }

        public ISaveSection Section { get; }

        public SaveFormat Format { get; }

        public string Id => Section.Id;
    }

    /// <summary>A known section as a deep read found it: checked, migrated, ready to restore.</summary>
    internal sealed class ReadSection
    {
        public byte[] Bytes;
        public SaveNode Data;
        public string Text;
        public FileRef File;
        public int Generation;
        public readonly SaveAttachments Attachments = new();
        public readonly Dictionary<string, FileRef> AttachmentFiles = new(StringComparer.Ordinal);
    }

    /// <summary>A file the read found broken, with its bytes when it had any (for its copy aside).</summary>
    internal readonly struct BrokenFile
    {
        public BrokenFile(string file, byte[] bytes)
        {
            File = file;
            Bytes = bytes;
        }

        public string File { get; }

        public byte[] Bytes { get; }
    }

    /// <summary>The generation a read picked, or why there is none.</summary>
    internal sealed class Picked
    {
        public string Refused;
        public string Error;
        public int Generation;
        public SaveNode Manifest;
        public SaveNode RawManifest;
        public string Build;
        public string FellBack;
        public int? FellBackFrom;
        public readonly List<string> Notes = new();
        public readonly List<int> BrokenGenerations = new();
        public readonly List<BrokenFile> BrokenFiles = new();
        public List<SectionEntry> Entries;
        public readonly Dictionary<string, ReadSection> Known = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Picks a slot's generation: the newest manifest that reads and whose every file is whole; a newer format anywhere in
    /// it refuses the slot. A deep read also parses, migrates and checks every known section, and keeps the trees.
    /// </summary>
    internal static class SlotReading
    {
        public static async UniTask<Picked> ReadAsync(ISaveStore store, SaveArea area, string slot, IReadOnlyList<KnownSection> known,
            bool deep, bool metadataOnly = false, bool background = false, CancellationToken ct = default)
        {
            try
            {
                return await ReadCoreAsync(store, area, slot, known, deep, metadataOnly, background, ct);
            }
            finally
            {
                if (background)
                {
                    await UniTask.SwitchToMainThread();
                }
            }
        }

        private static async UniTask<Picked> ReadCoreAsync(ISaveStore store, SaveArea area, string slot,
            IReadOnlyList<KnownSection> known, bool deep, bool metadataOnly, bool background, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (background)
            {
                await UniTask.SwitchToThreadPool();
            }

            var picked = new Picked();
            IReadOnlyList<SaveEntry> entries = await store.ListAsync(area, slot + "/", ct);
            var manifests = new List<int>();
            for (int i = 0; i < entries.Count; i++)
            {
                int g = SlotFiles.ManifestGeneration(Leaf(entries[i], slot));
                if (g >= 0)
                {
                    manifests.Add(g);
                }
            }

            if (manifests.Count == 0)
            {
                picked.Error = entries.Count == 0 ? $"{slot}: no such slot" : $"{slot}: no manifest";
                return picked;
            }

            manifests.Sort();
            var broken = new List<string>();
            for (int m = manifests.Count - 1; m >= 0; m--)
            {
                ct.ThrowIfCancellationRequested();
                if (background)
                {
                    await UniTask.SwitchToThreadPool();
                }

                int generation = manifests[m];
                string file = SlotFiles.Manifest(generation);
                byte[] bytes = await store.ReadAsync(new SaveKey(area, slot + "/" + file), ct);
                if (bytes == null)
                {
                    continue;
                }

                string text = SlotFiles.Utf8.GetString(bytes);
                SaveRead read = SaveSlots.ManifestFormat.Read(text);
                if (read.Status == SaveReadStatus.Newer)
                {
                    picked.Refused = $"{slot}: {read.Error}";
                    picked.RawManifest = Raw(text);
                    picked.Generation = generation;
                    return picked;
                }

                List<SectionEntry> sections = null;
                string why = read.IsOk ? null : read.Error;
                if (why == null && !SlotFiles.TryEntries(read.Data, out sections, out why))
                {
                    why ??= "its sections are not a slot's";
                }

                if (why != null)
                {
                    Broken(picked, broken, generation, file, bytes, why);
                    continue;
                }

                if (background)
                {
                    await UniTask.SwitchToMainThread();
                }

                ct.ThrowIfCancellationRequested();
                string refused = Newer(read.Data, sections, known, read.Build);
                if (refused != null)
                {
                    picked.Refused = $"{slot}: {refused}";
                    picked.RawManifest = read.Data;
                    picked.Generation = generation;
                    return picked;
                }

                picked.Known.Clear();
                var notes = new List<string>();
                bool whole = true;
                Dictionary<string, ReadSection> files = null;
                if (!metadataOnly)
                {
                    if (background)
                    {
                        await UniTask.SwitchToThreadPool();
                    }

                    files = await ReadFilesAsync(store, area, slot, sections, picked, broken, generation, deep, ct);
                    if (background)
                    {
                        await UniTask.SwitchToMainThread();
                    }

                    ct.ThrowIfCancellationRequested();
                    if (files == null)
                    {
                        continue;
                    }
                }

                for (int s = 0; !metadataOnly && s < sections.Count && whole; s++)
                {
                    ct.ThrowIfCancellationRequested();
                    SectionEntry entry = sections[s];
                    KnownSection section = Find(known, entry.Id);
                    ReadSection readSection = files[entry.Id];
                    byte[] data = readSection.Bytes;
                    if (section == null)
                    {
                        notes.Add($"section {entry.Id} (v{entry.Version}) is unknown to this build: kept as it is");
                        continue;
                    }

                    if (!deep)
                    {
                        continue;
                    }

                    SaveRead sectionRead = section.Format.Read(readSection.Text);
                    if (sectionRead.Status == SaveReadStatus.Newer)
                    {
                        picked.Refused = $"{slot}: {entry.File.File}: {sectionRead.Error}";
                        picked.RawManifest = read.Data;
                        picked.Generation = generation;
                        return picked;
                    }

                    if (!sectionRead.IsOk)
                    {
                        Broken(picked, broken, generation, entry.File.File, data, sectionRead.Error);
                        whole = false;
                        break;
                    }

                    Add(notes, sectionRead.Notes, entry.Id);
                    if (section.Section is ISaveSectionCheck check)
                    {
                        SaveCheck result;
                        try
                        {
                            result = check.Check(sectionRead.Data);
                        }
                        catch (Exception e)
                        {
                            result = SaveCheck.Broken($"its check failed: {e.Message}");
                        }

                        if (result.Status == SaveCheckStatus.Newer)
                        {
                            picked.Refused = $"{slot}: {entry.File.File}: {result.Message}";
                            picked.RawManifest = read.Data;
                            picked.Generation = generation;
                            return picked;
                        }

                        if (result.Status == SaveCheckStatus.Broken)
                        {
                            Broken(picked, broken, generation, entry.File.File, data, result.Message);
                            whole = false;
                            break;
                        }

                        Add(notes, result.Notes, entry.Id);
                    }

                    readSection.Data = sectionRead.Data;
                    readSection.Bytes = null;
                    picked.Known[entry.Id] = readSection;
                }

                if (!whole)
                {
                    continue;
                }

                picked.Generation = generation;
                picked.Manifest = read.Data;
                picked.Build = read.Build;
                picked.Entries = sections;
                if (broken.Count > 0)
                {
                    picked.FellBackFrom = picked.BrokenGenerations[0];
                    picked.FellBack = $"{string.Join("; ", broken)}; generation {generation} used";
                    picked.Notes.Add($"{slot}: {picked.FellBack}");
                }

                picked.Notes.AddRange(notes);
                return picked;
            }

            picked.Known.Clear();
            picked.Error = broken.Count > 0 ? $"{slot}: no whole generation ({string.Join("; ", broken)})" : $"{slot}: no manifest";
            return picked;
        }

        // Only bytes, text and CRCs here. Game-owned formats, checks and kind catalogs stay on the caller's thread.
        private static async UniTask<Dictionary<string, ReadSection>> ReadFilesAsync(ISaveStore store, SaveArea area,
            string slot, List<SectionEntry> sections, Picked picked, List<string> broken, int generation, bool deep, CancellationToken ct)
        {
            var files = new Dictionary<string, ReadSection>(StringComparer.Ordinal);
            foreach (SectionEntry entry in sections)
            {
                ct.ThrowIfCancellationRequested();
                byte[] data = await store.ReadAsync(new SaveKey(area, slot + "/" + entry.File.File), ct);
                if (!entry.File.Matches(data))
                {
                    Broken(picked, broken, generation, entry.File.File, data,
                        data == null ? "missing" : "its length or CRC is not the manifest's");
                    return null;
                }

                var section = new ReadSection
                {
                    Bytes = deep ? data : null,
                    Text = deep ? SlotFiles.Utf8.GetString(data) : null,
                    File = entry.File,
                    Generation = SlotFiles.Generation(entry.File.File),
                };
                foreach (KeyValuePair<string, FileRef> attachment in entry.Attachments)
                {
                    ct.ThrowIfCancellationRequested();
                    FileRef bin = attachment.Value;
                    byte[] payload = await store.ReadAsync(new SaveKey(area, slot + "/" + bin.File), ct);
                    if (!bin.Matches(payload))
                    {
                        Broken(picked, broken, generation, bin.File, payload,
                            payload == null ? "missing" : "its length or CRC is not the manifest's");
                        return null;
                    }

                    if (deep)
                    {
                        section.Attachments.Set(attachment.Key, payload);
                        section.AttachmentFiles[attachment.Key] = bin;
                    }
                }

                files[entry.Id] = section;
            }

            return files;
        }

        public static string Leaf(SaveEntry entry, string slot) => entry.Key.Name.Substring(slot.Length + 1);

        public static KnownSection Find(IReadOnlyList<KnownSection> known, string id)
        {
            if (known == null)
            {
                return null;
            }

            for (int i = 0; i < known.Count; i++)
            {
                if (known[i].Id == id)
                {
                    return known[i];
                }
            }

            return null;
        }

        // A known section or kind newer than this build refuses the slot; unknown ones do not.
        private static string Newer(SaveNode manifest, List<SectionEntry> sections, IReadOnlyList<KnownSection> known, string build)
        {
            string who = build ?? "an unknown build";
            for (int i = 0; i < sections.Count; i++)
            {
                KnownSection section = Find(known, sections[i].Id);
                if (section != null && sections[i].Version > section.Format.Version)
                {
                    return $"made by {who} with {sections[i].Id} v{sections[i].Version}; this build knows v{section.Format.Version}";
                }
            }

            Dictionary<string, int> kinds = null;
            for (int i = 0; known != null && i < known.Count; i++)
            {
                if (!(known[i].Section is ISaveSectionKinds withKinds) || withKinds.Known == null)
                {
                    continue;
                }

                kinds ??= SlotFiles.Kinds(manifest);
                foreach (KeyValuePair<string, int> kind in withKinds.Known)
                {
                    if (kinds.TryGetValue(kind.Key, out int version) && version > kind.Value)
                    {
                        return $"made by {who} with {kind.Key} v{version}; this build knows v{kind.Value}";
                    }
                }
            }

            return null;
        }

        private static void Broken(Picked picked, List<string> broken, int generation, string file, byte[] bytes, string why)
        {
            broken.Add($"{file} is broken ({why})");
            picked.BrokenGenerations.Add(generation);
            picked.BrokenFiles.Add(new BrokenFile(file, bytes));
        }

        private static void Add(List<string> into, IReadOnlyList<string> notes, string id)
        {
            for (int i = 0; notes != null && i < notes.Count; i++)
            {
                into.Add(notes[i].StartsWith(id, StringComparison.Ordinal) ? notes[i] : $"{id}: {notes[i]}");
            }
        }

        private static SaveNode Raw(string text) => SaveJson.TryRead(text, out SaveNode root, out _) && root.IsObject ? root : null;
    }
}
