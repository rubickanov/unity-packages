using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Rubickanov.Save.Data;
using UnityEngine;

namespace Rubickanov.Save.World
{
    /// <summary>
    /// The world slots of one area: a slot is a folder <c>&lt;id&gt;/</c> of generations, each a manifest over its
    /// sections' files. The game makes one, opens a <see cref="SaveWorld"/> per scene and lists, creates, renames and
    /// deletes slots here. Every write of a slot (a save, a rename) waits its turn behind the others.
    /// </summary>
    public sealed class SaveSlots
    {
        private static readonly SaveMigrations SlotMigrations = new();

        private readonly Dictionary<string, SlotState> _states = new(StringComparer.Ordinal);
        private IReadOnlyList<KnownSection> _known = Array.Empty<KnownSection>();

        /// <param name="build">Written into every file: the build that wrote it.</param>
        /// <param name="logError">Where failures are told; Unity's error log when null.</param>
        /// <param name="keep">How many whole generations a slot keeps.</param>
        public SaveSlots(ISaveStore store, SaveArea area, string build, Action<string> logError = null, int keep = 3)
        {
            if (!area.IsValid || area.Stem != null)
            {
                throw new ArgumentException("Slots live in an area of their own folder.", nameof(area));
            }

            if (keep < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(keep), "A slot keeps at least one generation.");
            }

            Store = store ?? throw new ArgumentNullException(nameof(store));
            Area = area;
            Build = build ?? string.Empty;
            LogError = logError ?? Debug.LogError;
            Keep = keep;
        }

        /// <summary>The manifest's format ($kind "slot"), with its own chain of versions.</summary>
        public static SaveFormat ManifestFormat { get; } = new("slot", SlotMigrations);

        internal ISaveStore Store { get; }

        internal SaveArea Area { get; }

        internal string Build { get; }

        internal Action<string> LogError { get; }

        internal int Keep { get; }

        /// <summary>Every slot, the last saved first. Reads only; a refused or broken slot is listed with why.</summary>
        public async UniTask<IReadOnlyList<SlotInfo>> ListAsync()
        {
            IReadOnlyList<SaveEntry> entries = await Store.ListAsync(Area);
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                string name = entries[i].Key.Name;
                int slash = name.IndexOf('/');
                if (slash > 0 && seen.Add(name.Substring(0, slash)))
                {
                    ids.Add(name.Substring(0, slash));
                }
            }

            var slots = new List<SlotInfo>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                slots.Add(Info(ids[i], await SlotReading.ReadAsync(Store, Area, ids[i], _known, false)));
            }

            slots.Sort((a, b) => b.Saved.CompareTo(a.Saved));
            return slots;
        }

        /// <summary>The slot <paramref name="slot"/>; null when there is none. Reads only.</summary>
        public async UniTask<SlotInfo> FindAsync(string slot)
        {
            if (!IsSlotId(slot))
            {
                return null;
            }

            Picked picked = await SlotReading.ReadAsync(Store, Area, slot, _known, false);
            return picked.Error == $"{slot}: no such slot" ? null : Info(slot, picked);
        }

        /// <summary>A new empty slot (generation 1, no sections) with a new 8-hex id.</summary>
        public async UniTask<SlotInfo> CreateAsync(string name)
        {
            string id;
            do
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            while ((await Store.ListAsync(Area, id + "/")).Count > 0);

            DateTime now = DateTime.UtcNow;
            SaveWriter w = SaveWriter.Over(null);
            w.String("name", name ?? string.Empty);
            w.Time("created", now);
            w.Time("saved", now);
            w.Double("played", 0);
            w.Long("generation", 1);
            w.Object("sections");
            w.Object("kinds");
            SlotState state = State(id);
            await state.Turn.WaitAsync().ConfigureAwait(false);
            try
            {
                state.Issue(Array.Empty<SaveEntry>(), id);
                await Store.WriteAsync(Key(id, SlotFiles.Manifest(1)), SlotFiles.Utf8.GetBytes(ManifestFormat.Write(w.Tree, Build, LogError)));
            }
            finally
            {
                state.Turn.Release();
            }

            return Info(id, await SlotReading.ReadAsync(Store, Area, id, _known, false));
        }

        /// <summary>
        /// A new generation of the same files under another name. False (and told) for no such slot, a refused slot, or a
        /// failed write.
        /// </summary>
        public async UniTask<bool> RenameAsync(string slot, string name)
        {
            if (!IsSlotId(slot))
            {
                return false;
            }

            SlotState state = State(slot);
            await state.Turn.WaitAsync().ConfigureAwait(false);
            try
            {
                Picked picked = await SlotReading.ReadAsync(Store, Area, slot, _known, false);
                if (picked.Refused != null || picked.Error != null)
                {
                    LogError($"{slot} is not renamed: {picked.Refused ?? picked.Error}");
                    return false;
                }

                IReadOnlyList<SaveEntry> entries = await Store.ListAsync(Area, slot + "/");
                int generation = state.Issue(entries, slot);
                SaveWriter w = SaveWriter.Over(picked.Manifest);
                w.String("name", name ?? string.Empty);
                w.Long("generation", generation);
                await Store.WriteAsync(Key(slot, SlotFiles.Manifest(generation)), SlotFiles.Utf8.GetBytes(ManifestFormat.Write(w.Tree, Build, LogError)));
                lock (state.Gate)
                {
                    state.Name = name ?? string.Empty;
                }

                await SweepAsync(slot, generation, state);
                return true;
            }
            catch (SaveStoreException e)
            {
                LogError($"{slot} could not be renamed: {e.Message}");
                return false;
            }
            finally
            {
                state.Turn.Release();
            }
        }

        /// <summary>Every file of the slot. False (and told) for a slot open in a world, a refused slot or no such slot.</summary>
        public async UniTask<bool> DeleteAsync(string slot)
        {
            if (!IsSlotId(slot))
            {
                return false;
            }

            SlotState state = State(slot);
            lock (state.Gate)
            {
                if (state.World != null)
                {
                    LogError($"{slot} is open and is not deleted");
                    return false;
                }
            }

            await state.Turn.WaitAsync().ConfigureAwait(false);
            try
            {
                Picked picked = await SlotReading.ReadAsync(Store, Area, slot, _known, false);
                if (picked.Refused != null)
                {
                    LogError($"{slot} is not deleted: {picked.Refused}");
                    return false;
                }

                IReadOnlyList<SaveEntry> entries = await Store.ListAsync(Area, slot + "/");
                for (int i = 0; i < entries.Count; i++)
                {
                    await Store.DeleteAsync(entries[i].Key);
                }

                return entries.Count > 0;
            }
            catch (SaveStoreException e)
            {
                LogError($"{slot} could not be deleted: {e.Message}");
                return false;
            }
            finally
            {
                state.Turn.Release();
            }
        }

        /// <summary>
        /// A world over these slots with the game's sections, in the order they restore; <paramref name="describe"/> writes
        /// the little the list shows (scene, day, cash) into the manifest. Throws for ids that are not <c>[a-z0-9-]</c>, are
        /// <c>manifest</c> or repeat. Lists made after it also tell slots refused for these sections' versions.
        /// </summary>
        public SaveWorld Open(IReadOnlyList<ISaveSection> order, Action<SaveWriter> describe = null)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            var known = new List<KnownSection>(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                string id = order[i]?.Id;
                if (!SaveAttachments.IsPlain(id) || id == SlotFiles.ManifestStem)
                {
                    throw new ArgumentException($"A section's id is [a-z0-9-] and not \"manifest\": \"{id}\".", nameof(order));
                }

                if (SlotReading.Find(known, id) != null)
                {
                    throw new ArgumentException($"Two sections are \"{id}\".", nameof(order));
                }

                known.Add(new KnownSection(order[i]));
            }

            _known = known;
            return new SaveWorld(this, known, describe);
        }

        internal SaveKey Key(string slot, string file) => new(Area, slot + "/" + file);

        internal SlotState State(string slot)
        {
            lock (_states)
            {
                if (!_states.TryGetValue(slot, out SlotState state))
                {
                    state = new SlotState();
                    _states[slot] = state;
                }

                return state;
            }
        }

        /// <summary>
        /// After generation <paramref name="committed"/> is in: keeps the newest whole manifests, deletes the rest (and the
        /// ones a load found broken), then every file no kept manifest names that is older than the commit. Copies of broken
        /// files stay.
        /// </summary>
        internal async UniTask SweepAsync(string slot, int committed, SlotState state)
        {
            IReadOnlyList<SaveEntry> entries = await Store.ListAsync(Area, slot + "/");
            var manifests = new List<int>();
            for (int i = 0; i < entries.Count; i++)
            {
                int g = SlotFiles.ManifestGeneration(SlotReading.Leaf(entries[i], slot));
                if (g >= 0)
                {
                    manifests.Add(g);
                }
            }

            manifests.Sort();
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            int kept = 0;
            for (int m = manifests.Count - 1; m >= 0; m--)
            {
                int generation = manifests[m];
                bool flagged;
                lock (state.Gate)
                {
                    flagged = state.Broken.Contains(generation);
                }

                if (kept < Keep && !flagged)
                {
                    byte[] bytes = await Store.ReadAsync(Key(slot, SlotFiles.Manifest(generation)));
                    SaveRead read = bytes != null ? ManifestFormat.Read(SlotFiles.Utf8.GetString(bytes)) : default;
                    if (bytes != null && read.IsOk && SlotFiles.TryEntries(read.Data, out List<SectionEntry> sections, out _))
                    {
                        kept++;
                        for (int s = 0; s < sections.Count; s++)
                        {
                            referenced.Add(sections[s].File.File);
                            for (int a = 0; a < sections[s].Attachments.Count; a++)
                            {
                                referenced.Add(sections[s].Attachments[a].Value.File);
                            }
                        }

                        continue;
                    }
                }

                if (generation < committed)
                {
                    await Store.DeleteAsync(Key(slot, SlotFiles.Manifest(generation)));
                    lock (state.Gate)
                    {
                        state.Broken.Remove(generation);
                    }
                }
            }

            for (int i = 0; i < entries.Count; i++)
            {
                string leaf = SlotReading.Leaf(entries[i], slot);
                int generation = SlotFiles.Generation(leaf);
                if (SlotFiles.ManifestGeneration(leaf) >= 0 || SlotFiles.IsBrokenCopy(leaf) || generation < 0 || generation >= committed ||
                    referenced.Contains(leaf))
                {
                    continue;
                }

                await Store.DeleteAsync(entries[i].Key);
            }
        }

        private static bool IsSlotId(string slot) => SaveAttachments.IsPlain(slot);

        private SlotInfo Info(string id, Picked picked)
        {
            SaveNode manifest = picked.Manifest ?? picked.RawManifest;
            SaveReader r = SaveReader.Of(manifest);
            var sections = new Dictionary<string, int>(StringComparer.Ordinal);
            if (picked.Entries != null)
            {
                for (int i = 0; i < picked.Entries.Count; i++)
                {
                    sections[picked.Entries[i].Id] = picked.Entries[i].Version;
                }
            }

            string preview = r.String("preview");
            return new SlotInfo
            {
                Id = id,
                Name = r.String("name", string.Empty),
                Build = picked.Build ?? r.String(SaveFormat.BuildKey),
                Created = r.Time("created"),
                Saved = r.Time("saved"),
                Played = TimeSpan.FromSeconds(Math.Max(0, r.Double("played"))),
                Generation = picked.Generation,
                Sections = sections,
                Kinds = SlotFiles.Kinds(manifest),
                Game = r.Object("game"),
                Preview = string.IsNullOrEmpty(preview) ? null : preview,
                Refused = picked.Refused,
                FellBack = picked.FellBack,
                Error = picked.Error,
            };
        }
    }

    /// <summary>What every writer of one slot shares: the turn, the generations given out, a rename, broken generations.</summary>
    internal sealed class SlotState
    {
        public readonly SemaphoreSlim Turn = new(1, 1);
        public readonly object Gate = new();
        public readonly HashSet<int> Broken = new();
        public int LastIssued;
        public string Name;
        public SaveWorld World;

        /// <summary>One more than every generation in the slot's files and every one given out before.</summary>
        public int Issue(IReadOnlyList<SaveEntry> entries, string slot)
        {
            lock (Gate)
            {
                int max = LastIssued;
                for (int i = 0; i < entries.Count; i++)
                {
                    int g = SlotFiles.Generation(SlotReading.Leaf(entries[i], slot));
                    if (g > max)
                    {
                        max = g;
                    }
                }

                LastIssued = max + 1;
                return LastIssued;
            }
        }
    }
}
