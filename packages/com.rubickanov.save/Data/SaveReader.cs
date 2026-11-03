using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Rubickanov.Save.Data
{
    /// <summary>
    /// Reads a system's values by key. A missing key gives the fallback quietly; a value of the wrong kind (a string where
    /// a number was written, a number past an int) gives the fallback and a note with its key path ("clock/day: a number
    /// expected, "three" found").
    /// </summary>
    public readonly struct SaveReader
    {
        private static readonly string[] TimeFormats = { SaveWriter.TimeFormat, "o" };

        private readonly SaveNode _node;
        private readonly string _path;
        private readonly List<string> _notes;
        private readonly SaveAttachments _attachments;

        private SaveReader(SaveNode node, string path, List<string> notes, SaveAttachments attachments = null)
        {
            _node = node != null && node.IsObject ? node : null;
            _path = path;
            _notes = notes;
            _attachments = attachments;
        }

        /// <param name="node">The object to read; anything else reads as empty.</param>
        /// <param name="notes">Where values of the wrong kind are told; null drops them.</param>
        /// <param name="path">What the notes' key paths start with, as the section's id.</param>
        public static SaveReader Of(SaveNode node, List<string> notes = null, string path = null) => new(node, path, notes);

        /// <summary>As <see cref="Of(SaveNode, List{string}, string)"/>, with the section's payloads for <see cref="Attachment"/>.</summary>
        public static SaveReader Of(SaveNode node, List<string> notes, string path, SaveAttachments attachments) =>
            new(node, path, notes, attachments);

        public bool IsEmpty => _node == null || _node.Count == 0;

        /// <summary>The object read; null when there is none.</summary>
        public SaveNode Tree => _node;

        public bool Has(string key) => _node?[key] != null;

        public int Int(string key, int fallback = 0)
        {
            SaveNode n = Value(key, SaveNodeKind.Number, "a number");
            if (n == null)
            {
                return fallback;
            }

            if (n.IsInteger && n.Long >= int.MinValue && n.Long <= int.MaxValue)
            {
                return (int)n.Long;
            }

            Note(key, "an int expected", n);
            return fallback;
        }

        public long Long(string key, long fallback = 0)
        {
            SaveNode n = Value(key, SaveNodeKind.Number, "a number");
            if (n == null)
            {
                return fallback;
            }

            if (n.IsInteger)
            {
                return n.Long;
            }

            Note(key, "an integer expected", n);
            return fallback;
        }

        public float Float(string key, float fallback = 0) => (float)Double(key, fallback);

        public double Double(string key, double fallback = 0)
        {
            SaveNode n = Value(key, SaveNodeKind.Number, "a number");
            return n?.Double ?? fallback;
        }

        public string String(string key, string fallback = null)
        {
            SaveNode n = Value(key, SaveNodeKind.String, "a string");
            return n?.String ?? fallback;
        }

        public bool Bool(string key, bool fallback = false)
        {
            SaveNode n = Value(key, SaveNodeKind.Bool, "true or false");
            return n?.Bool ?? fallback;
        }

        /// <summary>By name; a name the enum no longer has gives the fallback and a note.</summary>
        public T Enum<T>(string key, T fallback) where T : struct, Enum
        {
            SaveNode n = Value(key, SaveNodeKind.String, typeof(T).Name);
            if (n == null)
            {
                return fallback;
            }

            if (System.Enum.TryParse(n.String, false, out T value) && System.Enum.IsDefined(typeof(T), value))
            {
                return value;
            }

            Note(key, $"a {typeof(T).Name} expected", n);
            return fallback;
        }

        public Vector2 Vector2(string key, Vector2 fallback = default) =>
            Floats(key, 2, out float x, out float y, out _, out _) ? new Vector2(x, y) : fallback;

        public Vector3 Vector3(string key, Vector3 fallback = default) =>
            Floats(key, 3, out float x, out float y, out float z, out _) ? new Vector3(x, y, z) : fallback;

        /// <summary>Identity when there is none.</summary>
        public Quaternion Quaternion(string key) => Quaternion(key, UnityEngine.Quaternion.identity);

        public Quaternion Quaternion(string key, Quaternion fallback) =>
            Floats(key, 4, out float x, out float y, out float z, out float w) ? new Quaternion(x, y, z, w) : fallback;

        public Color Color(string key, Color fallback = default) =>
            Floats(key, 4, out float r, out float g, out float b, out float a) ? new Color(r, g, b, a) : fallback;

        /// <summary>A UTC time, as <see cref="SaveWriter.Time"/> writes it.</summary>
        public DateTime Time(string key, DateTime fallback = default)
        {
            SaveNode n = Value(key, SaveNodeKind.String, "a time");
            if (n == null)
            {
                return fallback;
            }

            if (DateTime.TryParseExact(n.String, TimeFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime time))
            {
                return time;
            }

            Note(key, "a time expected", n);
            return fallback;
        }

        /// <summary>A nested object; an empty reader when there is none.</summary>
        public SaveReader Object(string key)
        {
            SaveNode n = Value(key, SaveNodeKind.Object, "an object");
            return new SaveReader(n, Join(key), _notes);
        }

        /// <summary>An array of objects: <c>foreach (SaveReader link in r.List("links"))</c>.</summary>
        public SaveListReader List(string key) => new(Value(key, SaveNodeKind.Array, "an array"), Join(key), _notes);

        /// <summary>Entries by id: <c>foreach (SaveEntryReader kept in r.Map("kept"))</c>.</summary>
        public SaveMapReader Map(string key) => new(Value(key, SaveNodeKind.Object, "an object"), Join(key), _notes);

        /// <summary>The raw value under <paramref name="key"/>, null when there is none; not a copy: do not change it.</summary>
        public SaveNode Node(string key) => _node?[key];

        /// <summary>The section's payload named <paramref name="name"/>; null when there is none or this is a nested reader.</summary>
        public byte[] Attachment(string name) => _attachments?.Get(name);

        /// <summary>Tells a note about <paramref name="key"/> to the load's notes.</summary>
        public void Note(string key, string what) => _notes?.Add($"{Join(key)}: {what}");

        internal string Join(string key) => string.IsNullOrEmpty(_path) ? key : _path + "/" + key;

        // Null quietly for a missing key or a null; null and a note for a value of another kind.
        private SaveNode Value(string key, SaveNodeKind kind, string expected)
        {
            SaveNode n = _node?[key];
            if (n == null || n.IsNull)
            {
                return null;
            }

            if (n.Kind == kind)
            {
                return n;
            }

            Note(key, $"{expected} expected", n);
            return null;
        }

        private void Note(string key, string expected, SaveNode found) => Note(key, $"{expected}, {found.Describe()} found");

        private bool Floats(string key, int count, out float a, out float b, out float c, out float d)
        {
            a = b = c = d = 0;
            SaveNode n = Value(key, SaveNodeKind.Array, $"an array of {count} numbers");
            if (n == null)
            {
                return false;
            }

            bool ok = n.Count == count;
            for (int i = 0; i < n.Count && ok; i++)
            {
                ok = n[i].Kind == SaveNodeKind.Number;
            }

            if (!ok)
            {
                Note(key, $"an array of {count} numbers expected", n);
                return false;
            }

            a = (float)n[0].Double;
            b = (float)n[1].Double;
            c = count > 2 ? (float)n[2].Double : 0;
            d = count > 3 ? (float)n[3].Double : 0;
            return true;
        }
    }

    /// <summary>An array of objects; an item that is not an object is skipped with a note.</summary>
    public readonly struct SaveListReader
    {
        private readonly SaveNode _array;
        private readonly string _path;
        private readonly List<string> _notes;

        internal SaveListReader(SaveNode array, string path, List<string> notes)
        {
            _array = array;
            _path = path;
            _notes = notes;
        }

        public int Count => _array?.Count ?? 0;

        public Enumerator GetEnumerator() => new(this);

        public struct Enumerator
        {
            private readonly SaveListReader _list;
            private int _index;

            internal Enumerator(SaveListReader list)
            {
                _list = list;
                _index = -1;
                Current = default;
            }

            public SaveReader Current { get; private set; }

            public bool MoveNext()
            {
                while (_list._array != null && ++_index < _list._array.Count)
                {
                    SaveNode item = _list._array[_index];
                    string path = _list._path + "/" + _index.ToString(CultureInfo.InvariantCulture);
                    if (item.IsObject)
                    {
                        Current = SaveReader.Of(item, _list._notes, path);
                        return true;
                    }

                    _list._notes?.Add($"{path}: an object expected, {item.Describe()} found");
                }

                return false;
            }
        }
    }

    /// <summary>One entry of a map: its id and its object.</summary>
    public readonly struct SaveEntryReader
    {
        internal SaveEntryReader(string id, SaveReader value)
        {
            Id = id;
            Value = value;
        }

        public string Id { get; }
        public SaveReader Value { get; }
    }

    /// <summary>Entries by id, in ordinal order of the ids; an entry that is not an object is skipped with a note.</summary>
    public readonly struct SaveMapReader
    {
        private readonly SaveNode _map;
        private readonly string _path;
        private readonly List<string> _notes;

        internal SaveMapReader(SaveNode map, string path, List<string> notes)
        {
            _map = map;
            _path = path;
            _notes = notes;
        }

        public int Count => _map?.Count ?? 0;

        public bool Has(string id) => _map?[id] != null;

        /// <summary>The entry with <paramref name="id"/>; an empty reader when there is none.</summary>
        public SaveReader this[string id] => SaveReader.Of(_map?[id], _notes, _path + "/" + id);

        public Enumerator GetEnumerator() => new(this);

        public struct Enumerator
        {
            private readonly SaveMapReader _reader;
            private int _index;

            internal Enumerator(SaveMapReader reader)
            {
                _reader = reader;
                _index = -1;
                Current = default;
            }

            public SaveEntryReader Current { get; private set; }

            public bool MoveNext()
            {
                while (_reader._map != null && ++_index < _reader._map.Count)
                {
                    string id = _reader._map.Keys[_index];
                    SaveNode entry = _reader._map[id];
                    string path = _reader._path + "/" + id;
                    if (entry.IsObject)
                    {
                        Current = new SaveEntryReader(id, SaveReader.Of(entry, _reader._notes, path));
                        return true;
                    }

                    _reader._notes?.Add($"{path}: an object expected, {entry.Describe()} found");
                }

                return false;
            }
        }
    }
}
