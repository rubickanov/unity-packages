using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Rubickanov.Save.Data;

namespace Rubickanov.Save.World
{
    /// <summary>
    /// One slot open in a scene: loads it once, then saves it. A save captures every section on the calling (main)
    /// thread in one go, then builds the text and writes on the thread pool and never comes back to the main thread:
    /// completions and failures are handed over by <see cref="Tick"/>, which the game calls every frame. One write is in
    /// flight at a time; a save asked meanwhile is captured at once and waits, a newer one replaces it.
    /// </summary>
    public sealed class SaveWorld : IDisposable
    {
        private const int SaveNowWaitMs = 5000;

        private readonly SaveSlots _slots;
        private readonly Section[] _sections;
        private readonly Action<SaveWriter> _describe;
        private readonly object _gate = new();
        private readonly ManualResetEventSlim _idle = new(true);
        private readonly ConcurrentQueue<Action> _main = new();

        private SlotState _state;
        private string _refused;
        private bool _closed;
        private bool _inFlight;
        private Shot _waiting;
        private SaveNode _manifestBase;
        private int _committed;
        private double _playedAtLoad;
        private DateTime _loadedAt;

        internal SaveWorld(SaveSlots slots, IReadOnlyList<KnownSection> known, Action<SaveWriter> describe)
        {
            _slots = slots;
            _describe = describe;
            Known = known;
            _sections = new Section[known.Count];
            for (int i = 0; i < known.Count; i++)
            {
                _sections[i] = new Section(known[i]);
            }
        }

        /// <summary>The slot loaded; null before <see cref="LoadAsync"/>.</summary>
        public string Slot { get; private set; }

        /// <summary>Whether the slot loaded and saves go to it.</summary>
        public bool Loaded { get; private set; }

        /// <summary>Why the slot is refused; null when it is not.</summary>
        public string Refused => _refused;

        internal IReadOnlyList<KnownSection> Known { get; }

        /// <summary>
        /// Reads the slot's newest whole generation (falling back past broken ones, keeping copies of broken files aside),
        /// refuses it whole when anything in it is newer than this build, then restores every section the slot has, in the
        /// game's order, on the calling thread. Once per world.
        /// </summary>
        public UniTask<SaveReport> LoadAsync(string slot) => LoadCoreAsync(slot, false, default);

        /// <summary>
        /// Loads with file reads and CRCs on the pool, game migrations, checks and restore on Unity's main thread.
        /// Cancellation or Close before restore prevents any section from being restored. Call from the main thread.
        /// </summary>
        public async UniTask<SaveReport> LoadInBackgroundAsync(string slot, CancellationToken ct = default)
        {
            try
            {
                return await LoadCoreAsync(slot, true, ct);
            }
            catch (OperationCanceledException)
            {
                Close();
                throw;
            }
        }

        private async UniTask<SaveReport> LoadCoreAsync(string slot, bool background, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_closed)
            {
                throw new ObjectDisposedException(nameof(SaveWorld));
            }

            if (Slot != null)
            {
                throw new InvalidOperationException($"This world already loaded {Slot}.");
            }

            Slot = slot ?? throw new ArgumentNullException(nameof(slot));
            _state = _slots.State(slot);
            lock (_state.Gate)
            {
                if (_state.World != null)
                {
                    return Fail(new SaveReport(), $"{slot} is open in another world");
                }

                _state.World = this;
            }

            var report = new SaveReport { Notes = Array.Empty<string>() };
            var reading = Stopwatch.StartNew();
            Picked picked;
            try
            {
                picked = await SlotReading.ReadAsync(_slots.Store, _slots.Area, slot, Known, true,
                    background: background, ct: ct);
            }
            catch (SaveStoreException e)
            {
                return Fail(report, $"{slot} could not be read: {e.Message}");
            }

            report.ReadMs = (float)reading.Elapsed.TotalMilliseconds;
            ct.ThrowIfCancellationRequested();
            if (_closed)
            {
                throw new OperationCanceledException("The world closed while loading.");
            }

            report.Generation = picked.Generation;
            report.FellBackFrom = picked.FellBackFrom;
            if (picked.Refused != null)
            {
                _refused = picked.Refused;
                report.Refused = picked.Refused;
                _slots.LogError($"{picked.Refused}; it is not loaded nor written");
                return report;
            }

            if (background && picked.BrokenFiles.Count > 0)
            {
                await UniTask.RunOnThreadPool(() => KeepBrokenAsync(picked, true), cancellationToken: ct);
                Tick();
            }
            else
            {
                await KeepBrokenAsync(picked);
            }

            ct.ThrowIfCancellationRequested();
            if (_closed)
            {
                throw new OperationCanceledException("The world closed while loading.");
            }

            if (picked.Error != null)
            {
                return Fail(report, picked.Error);
            }

            if (picked.FellBack != null)
            {
                _slots.LogError($"{slot}: {picked.FellBack}");
            }

            var notes = new List<string>(picked.Notes);
            for (int i = 0; i < _sections.Length; i++)
            {
                if (picked.Known.TryGetValue(_sections[i].Known.Id, out ReadSection read))
                {
                    _sections[i].Loaded(read);
                }
            }

            _manifestBase = picked.Manifest;
            _committed = picked.Generation;
            _playedAtLoad = Math.Max(0, SaveReader.Of(picked.Manifest).Double("played"));
            _loadedAt = DateTime.UtcNow;

            var watch = Stopwatch.StartNew();
            for (int i = 0; i < _sections.Length; i++)
            {
                Section section = _sections[i];
                if (section.Base == null)
                {
                    continue;
                }

                try
                {
                    section.Known.Section.Restore(SaveReader.Of(section.Base, notes, section.Known.Id, section.Attachments));
                }
                catch (Exception e)
                {
                    string message = $"{slot}: {section.Known.Id} could not be restored: {e.Message}";
                    notes.Add(message);
                    _slots.LogError(message);
                }
            }

            report.RestoreMs = (float)watch.Elapsed.TotalMilliseconds;
            report.Notes = notes;
            Loaded = true;
            return report;
        }

        /// <summary>Captures now and writes off the main thread; a no-op before a load, for a refused slot and after <see cref="Close"/>.</summary>
        public void Save()
        {
            if (CanSave())
            {
                Enqueue(Capture(), null);
            }
        }

        /// <summary>As <see cref="Save"/>; true once the generation is in. Completes in <see cref="Tick"/>.</summary>
        public UniTask<bool> SaveAsync()
        {
            if (!CanSave())
            {
                return UniTask.FromResult(false);
            }

            var done = new UniTaskCompletionSource<bool>();
            Enqueue(Capture(), done);
            return done.Task;
        }

        /// <summary>
        /// For quitting: waits for the write in flight (at most 5 s, then tells it and writes a newer generation anyway),
        /// captures and writes on the calling thread. The store must answer off the main thread or on the calling one, as
        /// the desktop store does.
        /// </summary>
        public void SaveNow()
        {
            if (!CanSave())
            {
                return;
            }

            Shot stolen;
            lock (_gate)
            {
                stolen = _waiting;
                _waiting = null;
            }

            bool idle = _idle.Wait(SaveNowWaitMs);
            if (!idle)
            {
                _slots.LogError($"{Slot}: the save in flight is still writing after {SaveNowWaitMs / 1000} s; a newer generation is written beside it");
            }

            Shot shot = Capture();
            if (stolen != null)
            {
                shot.Waiters.AddRange(stolen.Waiters);
            }

            bool turn = idle && _state.Turn.Wait(SaveNowWaitMs);
            bool ok;
            try
            {
                UniTask<bool> commit = CommitAsync(shot);
                ok = commit.Status.IsCompleted() ? commit.GetAwaiter().GetResult() : commit.AsTask().GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Post($"{Slot}: the save could not be written: {e.Message}");
                ok = false;
            }
            finally
            {
                if (turn)
                {
                    _state.Turn.Release();
                }
            }

            Complete(shot, ok);
            Tick();
        }

        /// <summary>Hands over finished saves and their failures; call it every frame from the main thread.</summary>
        public void Tick()
        {
            while (_main.TryDequeue(out Action action))
            {
                action();
            }
        }

        /// <summary>Later saves do nothing; a write in flight still lands. The slot may be deleted afterwards.</summary>
        public void Close()
        {
            _closed = true;
            if (_state != null)
            {
                lock (_state.Gate)
                {
                    if (_state.World == this)
                    {
                        _state.World = null;
                    }
                }
            }
        }

        public void Dispose() => Close();

        private SaveReport Fail(SaveReport report, string error)
        {
            report.Error = error;
            report.Notes ??= Array.Empty<string>();
            _slots.LogError(error);
            return report;
        }

        private bool CanSave()
        {
            if (_closed || _refused != null)
            {
                return false;
            }

            if (!Loaded)
            {
                _slots.LogError("A world saves only after its slot loaded.");
                return false;
            }

            return true;
        }

        private async UniTask KeepBrokenAsync(Picked picked, bool background = false)
        {
            lock (_state.Gate)
            {
                for (int i = 0; i < picked.BrokenGenerations.Count; i++)
                {
                    _state.Broken.Add(picked.BrokenGenerations[i]);
                }
            }

            for (int i = 0; i < picked.BrokenFiles.Count; i++)
            {
                BrokenFile broken = picked.BrokenFiles[i];
                if (broken.Bytes == null)
                {
                    continue;
                }

                SaveKey copy = _slots.Key(Slot, SlotFiles.BrokenCopy(broken.File));
                try
                {
                    if (await _slots.Store.FindAsync(copy) == null)
                    {
                        await _slots.Store.WriteAsync(copy, broken.Bytes);
                    }
                }
                catch (SaveStoreException e)
                {
                    string message = $"{Slot}: {broken.File} could not be kept aside: {e.Message}";
                    if (background)
                    {
                        Post(message);
                    }
                    else
                    {
                        _slots.LogError(message);
                    }
                }
            }
        }

        // Main thread: every section over a copy of its base, the new trees becoming the bases.
        private Shot Capture()
        {
            var shot = new Shot
            {
                Trees = new SaveNode[_sections.Length],
                Attachments = new SaveAttachments[_sections.Length],
                Saved = DateTime.UtcNow,
            };
            lock (_gate)
            {
                shot.ManifestBase = _manifestBase;
            }

            shot.Played = _playedAtLoad + (shot.Saved - _loadedAt).TotalSeconds;
            for (int i = 0; i < _sections.Length; i++)
            {
                Section section = _sections[i];
                var attachments = new SaveAttachments();
                SaveWriter w = SaveWriter.Over(section.Base, attachments);
                try
                {
                    section.Known.Section.Capture(w);
                    section.Base = w.Tree;
                    section.Attachments = attachments;
                }
                catch (Exception e)
                {
                    _slots.LogError($"{Slot}: {section.Known.Id} could not be captured, its last state is written: {e.Message}");
                    section.Base ??= SaveNode.NewObject();
                }

                shot.Trees[i] = section.Base;
                shot.Attachments[i] = section.Attachments;
                if (section.Known.Section is ISaveSectionKinds kinds)
                {
                    shot.Kinds ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    kinds.Collect(section.Base, shot.Kinds);
                }
            }

            if (_describe != null)
            {
                SaveWriter game = SaveWriter.Over(shot.ManifestBase?["game"]);
                try
                {
                    _describe(game);
                    shot.Game = game.Tree;
                }
                catch (Exception e)
                {
                    _slots.LogError($"{Slot}: the slot's description could not be written: {e.Message}");
                }
            }

            return shot;
        }

        private void Enqueue(Shot shot, UniTaskCompletionSource<bool> done)
        {
            lock (_gate)
            {
                if (done != null)
                {
                    shot.Waiters.Add(done);
                }

                if (_inFlight)
                {
                    if (_waiting != null)
                    {
                        shot.Waiters.AddRange(_waiting.Waiters);
                    }

                    _waiting = shot;
                    return;
                }

                _inFlight = true;
                _idle.Reset();
            }

            RunAsync(shot).Forget();
        }

        private async UniTaskVoid RunAsync(Shot shot)
        {
            try
            {
                await UniTask.SwitchToThreadPool();
                while (shot != null)
                {
                    bool ok = false;
                    await _state.Turn.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        ok = await CommitAsync(shot);
                    }
                    catch (Exception e)
                    {
                        Post($"{Slot}: the save could not be written: {e.Message}");
                    }
                    finally
                    {
                        _state.Turn.Release();
                    }

                    Complete(shot, ok);
                    lock (_gate)
                    {
                        shot = _waiting;
                        _waiting = null;
                        if (shot == null)
                        {
                            _inFlight = false;
                            _idle.Set();
                        }
                    }
                }
            }
            finally
            {
                // Only an unexpected throw leaves the loop with a shot: nothing may stay in flight for ever.
                if (shot != null)
                {
                    Shot left;
                    lock (_gate)
                    {
                        left = _waiting;
                        _waiting = null;
                        _inFlight = false;
                        _idle.Set();
                    }

                    Complete(shot, false);
                    if (left != null)
                    {
                        Complete(left, false);
                    }
                }
            }
        }

        // Any thread: text, checksums, new files, the manifest last (the commit), then the sweep.
        private async UniTask<bool> CommitAsync(Shot shot)
        {
            ISaveStore store = _slots.Store;
            string build = _slots.Build;
            int generation;
            try
            {
                generation = _state.Issue(await store.ListAsync(_slots.Area, Slot + "/"), Slot);
            }
            catch (SaveStoreException e)
            {
                Post($"{Slot}: the save could not list the slot: {e.Message}");
                return false;
            }

            var fresh = new Verified[_sections.Length];
            var freshBins = new Dictionary<string, FileRef>[_sections.Length];
            SaveWriter manifest = SaveWriter.Over(shot.ManifestBase);
            string name;
            lock (_state.Gate)
            {
                name = _state.Name;
            }

            if (name != null)
            {
                manifest.String("name", name);
            }

            if (manifest.Tree["created"] == null)
            {
                manifest.Time("created", shot.Saved);
            }

            manifest.Long("generation", generation);
            manifest.Time("saved", shot.Saved);
            manifest.Double("played", Math.Round(shot.Played, 3));
            if (shot.Game != null)
            {
                manifest.Node("game", shot.Game);
            }

            if (shot.Kinds != null)
            {
                SaveWriter kinds = SaveWriter.Over(null);
                foreach (KeyValuePair<string, int> kind in shot.Kinds)
                {
                    kinds.Int(kind.Key, kind.Value);
                }

                manifest.Node("kinds", kinds.Tree);
            }

            SaveWriter sections = manifest.Object("sections");
            var errors = new List<string>();
            try
            {
                for (int i = 0; i < _sections.Length; i++)
                {
                    Section section = _sections[i];
                    string id = section.Known.Id;
                    string text = section.Known.Format.Write(shot.Trees[i], build, errors.Add);
                    FileRef file;
                    Verified verified;
                    lock (_gate)
                    {
                        verified = section.Verified;
                    }

                    if (verified != null && verified.Text == text)
                    {
                        file = verified.File;
                    }
                    else
                    {
                        byte[] bytes = SlotFiles.Utf8.GetBytes(text);
                        file = new FileRef(SlotFiles.Section(id, generation), bytes.LongLength, SlotFiles.Crc32(bytes));
                        await store.WriteAsync(_slots.Key(Slot, file.File), bytes);
                        fresh[i] = new Verified(text, file, generation);
                    }

                    SaveWriter entry = sections.Object(id);
                    file.WriteTo(entry);
                    entry.Int("version", section.Known.Format.Version);
                    entry.Remove("attachments");
                    SaveAttachments attachments = shot.Attachments[i];
                    if (attachments != null && attachments.Count > 0)
                    {
                        SaveMapWriter bins = entry.Map("attachments");
                        var refs = new Dictionary<string, FileRef>(StringComparer.Ordinal);
                        foreach (KeyValuePair<string, byte[]> payload in attachments.All)
                        {
                            uint crc = SlotFiles.Crc32(payload.Value);
                            FileRef bin;
                            lock (_gate)
                            {
                                section.AttachmentFiles.TryGetValue(payload.Key, out bin);
                            }

                            if (bin.File == null || bin.Size != payload.Value.LongLength || bin.Crc != crc)
                            {
                                bin = new FileRef(SlotFiles.Attachment(id, payload.Key, generation), payload.Value.LongLength, crc);
                                await store.WriteAsync(_slots.Key(Slot, bin.File), payload.Value);
                            }

                            refs[payload.Key] = bin;
                            bin.WriteTo(bins.Entry(payload.Key));
                        }

                        freshBins[i] = refs;
                    }
                }

                string manifestText = SaveSlots.ManifestFormat.Write(manifest.Tree, build, errors.Add);
                await store.WriteAsync(_slots.Key(Slot, SlotFiles.Manifest(generation)), SlotFiles.Utf8.GetBytes(manifestText));
            }
            catch (SaveStoreException e)
            {
                Post($"{Slot}: generation {generation} could not be written, the slot stays at its last one: {e.Message}");
                return false;
            }
            finally
            {
                for (int i = 0; i < errors.Count; i++)
                {
                    Post($"{Slot}: {errors[i]}");
                }
            }

            lock (_gate)
            {
                for (int i = 0; i < _sections.Length; i++)
                {
                    Section section = _sections[i];
                    if (fresh[i] != null && (section.Verified == null || section.Verified.Generation < generation))
                    {
                        section.Verified = fresh[i];
                    }

                    if (generation > section.AttachmentsGeneration)
                    {
                        section.AttachmentsGeneration = generation;
                        section.AttachmentFiles.Clear();
                        if (freshBins[i] != null)
                        {
                            foreach (KeyValuePair<string, FileRef> bin in freshBins[i])
                            {
                                section.AttachmentFiles[bin.Key] = bin.Value;
                            }
                        }
                    }
                }

                if (generation > _committed)
                {
                    _committed = generation;
                    _manifestBase = manifest.Tree;
                }
            }

            try
            {
                await _slots.SweepAsync(Slot, generation, _state);
            }
            catch (SaveStoreException e)
            {
                Post($"{Slot}: old generations could not be swept: {e.Message}");
            }

            return true;
        }

        private void Complete(Shot shot, bool ok)
        {
            for (int i = 0; i < shot.Waiters.Count; i++)
            {
                UniTaskCompletionSource<bool> done = shot.Waiters[i];
                _main.Enqueue(() => done.TrySetResult(ok));
            }
        }

        private void Post(string message)
        {
            Action<string> log = _slots.LogError;
            _main.Enqueue(() => log(message));
        }

        /// <summary>Text this process read and checked, or wrote: a file that is whole for sure.</summary>
        private sealed class Verified
        {
            public Verified(string text, FileRef file, int generation)
            {
                Text = text;
                File = file;
                Generation = generation;
            }

            public string Text { get; }

            public FileRef File { get; }

            public int Generation { get; }
        }

        private sealed class Section
        {
            public Section(KnownSection known) => Known = known;

            public KnownSection Known { get; }

            /// <summary>The tree read at load, then each capture's: what the next capture writes over.</summary>
            public SaveNode Base;

            public SaveAttachments Attachments = new();
            public Verified Verified;
            public readonly Dictionary<string, FileRef> AttachmentFiles = new(StringComparer.Ordinal);
            public int AttachmentsGeneration;

            public void Loaded(ReadSection read)
            {
                Base = read.Data;
                Attachments = read.Attachments;
                Verified = new Verified(read.Text, read.File, read.Generation);
                AttachmentsGeneration = read.Generation;
                foreach (KeyValuePair<string, FileRef> bin in read.AttachmentFiles)
                {
                    AttachmentFiles[bin.Key] = bin.Value;
                }
            }
        }

        private sealed class Shot
        {
            public SaveNode[] Trees;
            public SaveAttachments[] Attachments;
            public SaveNode ManifestBase;
            public SaveNode Game;
            public Dictionary<string, int> Kinds;
            public DateTime Saved;
            public double Played;
            public readonly List<UniTaskCompletionSource<bool>> Waiters = new();
        }
    }
}
