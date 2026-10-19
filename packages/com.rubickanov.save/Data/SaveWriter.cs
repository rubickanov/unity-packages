using System;
using System.Globalization;
using UnityEngine;

namespace Rubickanov.Save.Data
{
    /// <summary>
    /// Writes a system's values by key over a copy of what was there before (its base): a key the code does not write
    /// stays as it was, so fields of a newer build survive an older one. <see cref="Over"/> starts one;
    /// <see cref="Tree"/> is the result, the base of the next write.
    /// </summary>
    public readonly struct SaveWriter
    {
        internal const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

        private readonly SaveNode _node;

        internal SaveWriter(SaveNode node) => _node = node;

        /// <summary>
        /// A writer over a copy of <paramref name="baseTree"/>, which is not changed; over a new object when it is null
        /// or not an object.
        /// </summary>
        public static SaveWriter Over(SaveNode baseTree) =>
            new(baseTree != null && baseTree.IsObject ? baseTree.Clone() : SaveNode.NewObject());

        /// <summary>The object this writer writes: after a capture, the new tree and the next base.</summary>
        public SaveNode Tree => _node;

        public void Int(string key, int value) => _node[key] = SaveNode.Of(value);
        public void Long(string key, long value) => _node[key] = SaveNode.Of(value);
        public void Float(string key, float value) => _node[key] = SaveNode.Of(value);
        public void Double(string key, double value) => _node[key] = SaveNode.Of(value);
        public void String(string key, string value) => _node[key] = SaveNode.Of(value);
        public void Bool(string key, bool value) => _node[key] = SaveNode.Of(value);

        /// <summary>By name, so reordering the enum in code does not change what a file means.</summary>
        public void Enum<T>(string key, T value) where T : struct, Enum => _node[key] = SaveNode.Of(value.ToString());

        public void Vector2(string key, Vector2 v) => _node[key] = Floats(v.x, v.y);
        public void Vector3(string key, Vector3 v) => _node[key] = Floats(v.x, v.y, v.z);
        public void Quaternion(string key, Quaternion q) => _node[key] = Floats(q.x, q.y, q.z, q.w);
        public void Color(string key, Color c) => _node[key] = Floats(c.r, c.g, c.b, c.a);

        /// <summary>UTC, as "2026-10-20T15:00:00Z" (fractions of a second only when there are any).</summary>
        public void Time(string key, DateTime time)
        {
            DateTime utc = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : time;
            _node[key] = SaveNode.Of(utc.ToString(TimeFormat, CultureInfo.InvariantCulture));
        }

        /// <summary>A nested object, written into the one already under <paramref name="key"/>.</summary>
        public SaveWriter Object(string key)
        {
            SaveNode child = _node[key];
            if (child == null || !child.IsObject)
            {
                child = SaveNode.NewObject();
                _node[key] = child;
            }

            return new SaveWriter(child);
        }

        /// <summary>An array of objects written whole: what was there is replaced.</summary>
        public SaveListWriter List(string key)
        {
            SaveNode array = SaveNode.NewArray();
            _node[key] = array;
            return new SaveListWriter(array);
        }

        /// <summary>
        /// Entries by id the writer owns: an entry not written is gone, but each entry written is written into the old one
        /// with the same id, so its fields this build does not know stay.
        /// </summary>
        public SaveMapWriter Map(string key)
        {
            SaveNode old = _node[key];
            SaveNode map = SaveNode.NewObject();
            _node[key] = map;
            return new SaveMapWriter(map, old != null && old.IsObject ? old : null);
        }

        /// <summary>A raw subtree as it is (a copy is not made: do not change it afterwards).</summary>
        public void Node(string key, SaveNode node) => _node[key] = node ?? SaveNode.Null();

        public void Remove(string key) => _node.Remove(key);

        private static SaveNode Floats(params float[] values)
        {
            SaveNode array = SaveNode.NewArray();
            for (int i = 0; i < values.Length; i++)
            {
                array.Add(SaveNode.Of(values[i]));
            }

            return array;
        }
    }

    /// <summary>An array of objects being written whole; <see cref="Add"/> writes the next one.</summary>
    public readonly struct SaveListWriter
    {
        private readonly SaveNode _array;

        internal SaveListWriter(SaveNode array) => _array = array;

        public int Count => _array.Count;

        public SaveWriter Add()
        {
            SaveNode item = SaveNode.NewObject();
            _array.Add(item);
            return new SaveWriter(item);
        }
    }

    /// <summary>Entries by id; <see cref="Entry"/> writes one into its old self.</summary>
    public readonly struct SaveMapWriter
    {
        private readonly SaveNode _map;
        private readonly SaveNode _old;

        internal SaveMapWriter(SaveNode map, SaveNode old)
        {
            _map = map;
            _old = old;
        }

        public int Count => _map.Count;

        public SaveWriter Entry(string id)
        {
            SaveNode entry = _map[id];
            if (entry == null)
            {
                // The old entry is the writer's own copy already: taken over, not copied again.
                SaveNode old = _old?[id];
                entry = old != null && old.IsObject ? old : SaveNode.NewObject();
                _map[id] = entry;
            }

            return new SaveWriter(entry);
        }
    }
}
