using System;
using System.Collections.Generic;

namespace Rubickanov.Save.Data
{
    public enum SaveNodeKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    /// <summary>
    /// One value of a saved document: null, a bool, a number, a string, an array or an object. A number read as an integer
    /// (no '.', 'e' or 'E', fits 64 bits) is kept as a <see cref="long"/>, so ids, counters and money lose nothing; any
    /// other number is a <see cref="double"/>. Object keys are kept in ordinal order, the order they are written in.
    /// </summary>
    public sealed class SaveNode
    {
        private static readonly IReadOnlyList<string> NoKeys = new string[0];

        private readonly long _long;
        private readonly double _double;
        private readonly string _string;
        private readonly List<string> _keys;
        private readonly Dictionary<string, SaveNode> _values;
        private readonly List<SaveNode> _items;

        private SaveNode(SaveNodeKind kind, long l = 0, double d = 0, string s = null, bool integer = false, bool single = false)
        {
            Kind = kind;
            _long = l;
            _double = d;
            _string = s;
            IsInteger = integer;
            IsSingle = single;
            if (kind == SaveNodeKind.Object)
            {
                _keys = new List<string>();
                _values = new Dictionary<string, SaveNode>(StringComparer.Ordinal);
            }
            else if (kind == SaveNodeKind.Array)
            {
                _items = new List<SaveNode>();
            }
        }

        public SaveNodeKind Kind { get; }

        /// <summary>A number kept as a <see cref="long"/>.</summary>
        public bool IsInteger { get; }

        /// <summary>A number made from a <see cref="float"/>: written as the float's own shortest text.</summary>
        public bool IsSingle { get; }

        public bool IsNull => Kind == SaveNodeKind.Null;
        public bool IsObject => Kind == SaveNodeKind.Object;
        public bool IsArray => Kind == SaveNodeKind.Array;

        public static SaveNode Null() => new(SaveNodeKind.Null);
        public static SaveNode Of(bool value) => new(SaveNodeKind.Bool, value ? 1 : 0);
        public static SaveNode Of(long value) => new(SaveNodeKind.Number, value, value, integer: true);
        public static SaveNode Of(double value) => new(SaveNodeKind.Number, 0, value);
        public static SaveNode Of(float value) => new(SaveNodeKind.Number, 0, value, single: true);
        public static SaveNode Of(string value) => value == null ? Null() : new SaveNode(SaveNodeKind.String, s: value);
        public static SaveNode NewObject() => new(SaveNodeKind.Object);
        public static SaveNode NewArray() => new(SaveNodeKind.Array);

        public bool Bool => Kind == SaveNodeKind.Bool ? _long != 0 : throw WrongKind(SaveNodeKind.Bool);

        /// <summary>The number as a long; a non-integer is cut toward zero.</summary>
        public long Long => Kind == SaveNodeKind.Number ? (IsInteger ? _long : (long)_double) : throw WrongKind(SaveNodeKind.Number);

        public double Double => Kind == SaveNodeKind.Number ? (IsInteger ? _long : _double) : throw WrongKind(SaveNodeKind.Number);

        public string String => Kind == SaveNodeKind.String ? _string : throw WrongKind(SaveNodeKind.String);

        /// <summary>An object's value under <paramref name="key"/>, null when there is none; setting null removes the key.</summary>
        public SaveNode this[string key]
        {
            get
            {
                if (Kind != SaveNodeKind.Object)
                {
                    throw WrongKind(SaveNodeKind.Object);
                }

                return key != null && _values.TryGetValue(key, out SaveNode value) ? value : null;
            }
            set
            {
                if (key == null || key.IndexOf('/') >= 0)
                {
                    throw new ArgumentException($"A key holds no '/' and is not null: \"{key}\".", nameof(key));
                }

                if (value == null)
                {
                    Remove(key);
                    return;
                }

                Put(key, value);
            }
        }

        /// <summary>An object's keys, in ordinal order.</summary>
        public IReadOnlyList<string> Keys => Kind == SaveNodeKind.Object ? _keys : NoKeys;

        /// <summary>An array's items or an object's keys.</summary>
        public int Count => Kind switch
        {
            SaveNodeKind.Array => _items.Count,
            SaveNodeKind.Object => _keys.Count,
            _ => 0,
        };

        public SaveNode this[int index]
        {
            get => Kind == SaveNodeKind.Array ? _items[index] : throw WrongKind(SaveNodeKind.Array);
            set
            {
                if (Kind != SaveNodeKind.Array)
                {
                    throw WrongKind(SaveNodeKind.Array);
                }

                _items[index] = value ?? Null();
            }
        }

        public bool Remove(string key)
        {
            if (Kind != SaveNodeKind.Object)
            {
                throw WrongKind(SaveNodeKind.Object);
            }

            if (key == null || !_values.Remove(key))
            {
                return false;
            }

            _keys.RemoveAt(_keys.BinarySearch(key, StringComparer.Ordinal));
            return true;
        }

        public void Add(SaveNode item)
        {
            if (Kind != SaveNodeKind.Array)
            {
                throw WrongKind(SaveNodeKind.Array);
            }

            _items.Add(item ?? Null());
        }

        public void RemoveAt(int index)
        {
            if (Kind != SaveNodeKind.Array)
            {
                throw WrongKind(SaveNodeKind.Array);
            }

            _items.RemoveAt(index);
        }

        /// <summary>A deep copy.</summary>
        public SaveNode Clone()
        {
            switch (Kind)
            {
                case SaveNodeKind.Object:
                    SaveNode o = NewObject();
                    for (int i = 0; i < _keys.Count; i++)
                    {
                        string key = _keys[i];
                        o._keys.Add(key);
                        o._values.Add(key, _values[key].Clone());
                    }

                    return o;
                case SaveNodeKind.Array:
                    SaveNode a = NewArray();
                    for (int i = 0; i < _items.Count; i++)
                    {
                        a._items.Add(_items[i].Clone());
                    }

                    return a;
                default:
                    return this; // scalars never change
            }
        }

        /// <summary>The value at a path of object keys split by '/' ("till/cash"); null when any part is missing.</summary>
        public SaveNode At(string path)
        {
            SaveNode node = this;
            int start = 0;
            while (node != null)
            {
                int slash = path.IndexOf('/', start);
                string part = slash < 0 ? path.Substring(start) : path.Substring(start, slash - start);
                node = node.Kind == SaveNodeKind.Object ? node[part] : null;
                if (slash < 0)
                {
                    return node;
                }

                start = slash + 1;
            }

            return null;
        }

        public override string ToString() => SaveJson.Write(this);

        // Any key, '/' too: text read from elsewhere is kept as it came.
        internal void Put(string key, SaveNode value)
        {
            if (!_values.ContainsKey(key))
            {
                int at = _keys.BinarySearch(key, StringComparer.Ordinal);
                _keys.Insert(~at, key);
            }

            _values[key] = value;
        }

        internal double RawDouble => _double;

        private InvalidOperationException WrongKind(SaveNodeKind wanted) =>
            new($"{Describe()} is not {(wanted == SaveNodeKind.Array || wanted == SaveNodeKind.Object ? "an" : "a")} {wanted.ToString().ToLowerInvariant()}.");

        internal string Describe() => Kind switch
        {
            SaveNodeKind.Null => "null",
            SaveNodeKind.Bool => Bool ? "true" : "false",
            SaveNodeKind.Number => SaveJson.NumberText(this) ?? _double.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SaveNodeKind.String => "\"" + (_string.Length > 40 ? _string.Substring(0, 40) + "…" : _string) + "\"",
            SaveNodeKind.Array => "an array",
            _ => "an object",
        };
    }
}
