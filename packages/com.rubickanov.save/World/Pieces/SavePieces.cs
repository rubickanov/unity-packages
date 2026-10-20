using System;
using System.Collections.Generic;
using Rubickanov.Save.Data;
using UnityEngine;

namespace Rubickanov.Save.World
{
    /// <summary>
    /// The things of the world as one section, <c>pieces</c>: a map by SaveId of records <c>{"$v", "kind", …fields}</c>.
    /// The load's check migrates every record of a known kind before any section restores; restore spawns each through
    /// its kind's factory, then links pieces to each other. Records this build cannot spawn are kept as orphans and
    /// written back as they were.
    /// </summary>
    public sealed class SavePieces : ISaveSection, ISaveSectionCheck, ISaveSectionKinds
    {
        public const string SectionId = "pieces";

        internal const string VersionKey = "$v";
        internal const string KindKey = "kind";
        internal const string MapKey = "pieces";

        private readonly Action<string> _logError;
        private readonly Dictionary<string, PieceKind> _kinds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _known = new(StringComparer.Ordinal);
        private readonly Dictionary<SaveId, ISavePiece> _pieces = new();
        private readonly Dictionary<string, SaveNode> _orphans = new(StringComparer.Ordinal);

        public SavePieces(Action<string> logError = null) => _logError = logError ?? Debug.LogError;

        public string Id => SectionId;

        public SaveMigrations Migrations => null;

        public IReadOnlyDictionary<string, int> Known => _known;

        /// <summary>The pieces in the world now.</summary>
        public int Count => _pieces.Count;

        /// <summary>Records kept unread: a kind this build does not know, or a factory that returned null.</summary>
        public int Orphans => _orphans.Count;

        /// <summary>The orphan records by id, raw as the slot holds them; read only.</summary>
        public IReadOnlyDictionary<string, SaveNode> OrphanRecords => _orphans;

        public void Add(PieceKind kind)
        {
            if (kind == null)
            {
                throw new ArgumentNullException(nameof(kind));
            }

            if (_kinds.ContainsKey(kind.Kind))
            {
                throw new ArgumentException($"The kind \"{kind.Kind}\" is added twice.", nameof(kind));
            }

            _kinds.Add(kind.Kind, kind);
            _known.Add(kind.Kind, kind.Version);
        }

        /// <summary>A piece is in the world (spawned by default or loaded): it is saved from now on.</summary>
        public void Track(ISavePiece piece)
        {
            if (piece == null)
            {
                throw new ArgumentNullException(nameof(piece));
            }

            if (piece.SaveId.IsEmpty)
            {
                throw new ArgumentException($"A {piece.Kind} has no SaveId: give it SaveId.New() when it is first created.", nameof(piece));
            }

            if (_pieces.TryGetValue(piece.SaveId, out ISavePiece other) && !ReferenceEquals(other, piece))
            {
                _logError($"pieces: {piece.SaveId} is already a {other.Kind}; this {piece.Kind} is not saved");
                return;
            }

            if (!_kinds.ContainsKey(piece.Kind ?? ""))
            {
                _logError($"pieces: the kind \"{piece.Kind}\" is not added; {piece.SaveId} will load as an orphan");
            }

            _pieces[piece.SaveId] = piece;
            _orphans.Remove(piece.SaveId.ToString());
        }

        /// <summary>A piece is gone from the world: the next save drops it.</summary>
        public void Forget(ISavePiece piece)
        {
            if (piece != null && _pieces.TryGetValue(piece.SaveId, out ISavePiece tracked) && ReferenceEquals(tracked, piece))
            {
                _pieces.Remove(piece.SaveId);
            }
        }

        public ISavePiece Find(SaveId id) => _pieces.TryGetValue(id, out ISavePiece piece) ? piece : null;

        /// <summary>
        /// The piece whose SaveId is the string under <paramref name="key"/>; null when there is none (no note) or it is not
        /// in the world or not a <typeparamref name="T"/> (a note in the load's report).
        /// </summary>
        public T Resolve<T>(SaveReader r, string key) where T : class
        {
            string text = r.String(key);
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            if (!SaveId.TryParse(text, out SaveId id))
            {
                r.Note(key, $"\"{text}\" is not a SaveId; the link is dropped");
                return null;
            }

            ISavePiece piece = Find(id);
            if (piece == null)
            {
                r.Note(key, $"links to {text}, which is not in the world; the piece lives without the link");
                return null;
            }

            if (!(piece is T found))
            {
                r.Note(key, $"links to {text}, a {piece.Kind}, not a {typeof(T).Name}; the link is dropped");
                return null;
            }

            return found;
        }

        public void Capture(SaveWriter w)
        {
            SaveNode old = w.Tree[MapKey];
            if (old != null && !old.IsObject)
            {
                old = null;
            }

            SaveNode map = SaveNode.NewObject();
            foreach (KeyValuePair<string, SaveNode> orphan in _orphans)
            {
                map[orphan.Key] = orphan.Value;
            }

            foreach (KeyValuePair<SaveId, ISavePiece> pair in _pieces)
            {
                string id = pair.Key.ToString();
                SaveNode last = old?[id];
                ISavePiece piece = pair.Value;
                try
                {
                    SaveWriter record = SaveWriter.Over(last);
                    piece.Capture(record);
                    record.Int(VersionKey, _kinds.TryGetValue(piece.Kind ?? "", out PieceKind kind) ? kind.Version : 1);
                    record.String(KindKey, piece.Kind);
                    map[id] = record.Tree;
                }
                catch (Exception e)
                {
                    _logError($"pieces: {piece.Kind} {id} could not be captured, its last state is written: {e.Message}");
                    if (last != null && last.IsObject)
                    {
                        map[id] = last;
                    }
                }
            }

            w.Node(MapKey, map);
        }

        public void Restore(SaveReader r)
        {
            _orphans.Clear();
            SaveNode map = r.Tree?[MapKey];
            if (map == null || !map.IsObject)
            {
                return;
            }

            SaveMapReader records = r.Map(MapKey);
            var spawned = new List<KeyValuePair<ISavePiece, SaveReader>>();
            IReadOnlyList<string> keys = map.Keys;
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                SaveNode record = map[key];
                SaveReader reader = records[key];
                string kindName = KindOf(record);
                if (kindName == null || !SaveId.TryParse(key, out SaveId id))
                {
                    reader.Note(KindKey, "not a piece record (no kind or a bad id); kept as it is");
                    _orphans[key] = record;
                    continue;
                }

                if (_pieces.ContainsKey(id))
                {
                    reader.Note(KindKey, "already in the world; the saved record is not spawned");
                    continue;
                }

                if (!_kinds.TryGetValue(kindName, out PieceKind kind))
                {
                    reader.Note(KindKey, $"\"{kindName}\" is not known to this build; kept as it is");
                    _orphans[key] = record;
                    continue;
                }

                ISavePiece piece;
                try
                {
                    piece = kind.Spawn(id, reader);
                }
                catch (Exception e)
                {
                    _logError($"pieces: {kindName} {key} could not be spawned, kept as it is: {e.Message}");
                    piece = null;
                }

                if (piece == null)
                {
                    reader.Note(KindKey, $"the {kindName} could not be spawned; kept as it is");
                    _orphans[key] = record;
                    continue;
                }

                if (piece.SaveId != id)
                {
                    _logError($"pieces: the {kindName} factory gave {piece.SaveId} for {key}; the piece keeps its own id");
                }

                Track(piece);
                spawned.Add(new KeyValuePair<ISavePiece, SaveReader>(piece, reader));
            }

            for (int i = 0; i < spawned.Count; i++)
            {
                if (!(spawned[i].Key is ISavePieceLinks links))
                {
                    continue;
                }

                try
                {
                    links.Link(spawned[i].Value, this);
                }
                catch (Exception e)
                {
                    spawned[i].Value.Note(KindKey, $"its links failed: {e.Message}");
                    _logError($"pieces: {spawned[i].Key.Kind} {spawned[i].Key.SaveId} could not link: {e.Message}");
                }
            }
        }

        public SaveCheck Check(SaveNode data)
        {
            SaveNode map = data?[MapKey];
            if (map == null)
            {
                return SaveCheck.Ok;
            }

            if (!map.IsObject)
            {
                return SaveCheck.Broken($"{MapKey} is not an object");
            }

            List<string> notes = null;
            var keys = new string[map.Keys.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = map.Keys[i];
            }

            for (int i = 0; i < keys.Length; i++)
            {
                SaveNode record = map[keys[i]];
                string kindName = KindOf(record);
                if (kindName == null || !_kinds.TryGetValue(kindName, out PieceKind kind))
                {
                    continue;
                }

                SaveNode v = record[VersionKey];
                if (v == null || v.Kind != SaveNodeKind.Number || !v.IsInteger || v.Long < 1)
                {
                    return SaveCheck.Broken($"{MapKey}/{keys[i]}: a {kind.Kind} without a version");
                }

                if (v.Long > kind.Version)
                {
                    return SaveCheck.Newer($"{MapKey}/{keys[i]}: {kind.Kind} v{v.Long}; this build knows v{kind.Version}");
                }

                if (v.Long == kind.Version)
                {
                    continue;
                }

                try
                {
                    map[keys[i]] = kind.Migrate(record, (int)v.Long);
                }
                catch (Exception e)
                {
                    return SaveCheck.Broken($"{MapKey}/{keys[i]}: {kind.Kind} v{v.Long} could not migrate: {e.Message}");
                }

                notes ??= new List<string>();
                notes.Add($"{MapKey}/{keys[i]}: {kind.Kind} migrated from v{v.Long} to v{kind.Version}");
            }

            return notes == null ? SaveCheck.Ok : SaveCheck.OkWith(notes);
        }

        public void Collect(SaveNode data, Dictionary<string, int> into)
        {
            SaveNode map = data?[MapKey];
            if (map == null || !map.IsObject)
            {
                return;
            }

            IReadOnlyList<string> keys = map.Keys;
            for (int i = 0; i < keys.Count; i++)
            {
                SaveNode record = map[keys[i]];
                string kind = KindOf(record);
                if (kind == null)
                {
                    continue;
                }

                SaveNode v = record[VersionKey];
                int version = v != null && v.Kind == SaveNodeKind.Number && v.IsInteger ? (int)v.Long : 1;
                if (!into.TryGetValue(kind, out int had) || version > had)
                {
                    into[kind] = version;
                }
            }
        }

        private static string KindOf(SaveNode record)
        {
            SaveNode kind = record != null && record.IsObject ? record[KindKey] : null;
            return kind != null && kind.Kind == SaveNodeKind.String && kind.String.Length > 0 ? kind.String : null;
        }
    }
}
