using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Rubickanov.GameplayTags
{
    /// <summary>
    /// Mutable collection of gameplay tags with hierarchical and exact query support.
    /// Internally sorted for O(log n) exact lookups.
    /// </summary>
    public sealed class GameplayTagContainer : IEnumerable<GameplayTag>
    {
        private readonly List<int> _indices;
        private int _version;

        /// <summary>Number of tags in the container.</summary>
        public int Count => _indices.Count;

        /// <summary>True if the container has no tags.</summary>
        public bool IsEmpty => _indices.Count == 0;

        public GameplayTagContainer()
        {
            _indices = new List<int>();
        }

        public GameplayTagContainer(int capacity)
        {
            _indices = new List<int>(capacity);
        }

        /// <summary>Creates a container pre-filled with the given tags.</summary>
        public static GameplayTagContainer From(params GameplayTag[] tags)
        {
            var container = new GameplayTagContainer(tags.Length);
            foreach (var tag in tags)
                container.AddTag(tag);
            return container;
        }

        /// <summary>Adds a tag. No-op if already present or if tag is None.</summary>
        public void AddTag(GameplayTag tag)
        {
            if (tag.Index <= 0)
                return;

            var insertIndex = _indices.BinarySearch(tag.Index);
            if (insertIndex >= 0)
                return; // already present

            _indices.Insert(~insertIndex, tag.Index);
            _version++;
        }

        /// <summary>Removes a tag. Returns true if it was present.</summary>
        public bool RemoveTag(GameplayTag tag)
        {
            var index = _indices.BinarySearch(tag.Index);
            if (index < 0)
                return false;

            _indices.RemoveAt(index);
            _version++;
            return true;
        }

        public void Clear()
        {
            if (_indices.Count == 0)
                return;

            _indices.Clear();
            _version++;
        }

        /// <summary>
        /// Hierarchical query: returns true if any contained tag equals or descends from the query tag.
        /// <c>["Damage.Fire.DoT"].HasTag("Damage")</c> returns true.
        /// </summary>
        public bool HasTag(GameplayTag tag)
        {
            if (tag.Index <= 0)
                return false;

            var registry = GameplayTagRegistry.Instance;

            for (var i = 0; i < _indices.Count; i++)
            {
                if (registry.Matches(new GameplayTag(_indices[i]), tag))
                    return true;
            }

            return false;
        }

        /// <summary>Exact query: returns true only if the exact tag index is in the container.</summary>
        public bool HasTagExact(GameplayTag tag)
        {
            return _indices.BinarySearch(tag.Index) >= 0;
        }

        /// <summary>Returns true if this container satisfies every tag in <paramref name="other"/> (hierarchical). Empty other returns true.</summary>
        public bool HasAll(GameplayTagContainer other)
        {
            if (other.IsEmpty)
                return true;

            for (var i = 0; i < other._indices.Count; i++)
            {
                if (!HasTag(new GameplayTag(other._indices[i])))
                    return false;
            }

            return true;
        }

        /// <inheritdoc cref="HasAll(GameplayTagContainer)"/>
        public bool HasAll(ReadOnlyGameplayTagContainer other) => HasAll(other.Source);

        /// <summary>Returns true if this container satisfies at least one tag in <paramref name="other"/> (hierarchical). Empty other returns false.</summary>
        public bool HasAny(GameplayTagContainer other)
        {
            if (other.IsEmpty)
                return false;

            for (var i = 0; i < other._indices.Count; i++)
            {
                if (HasTag(new GameplayTag(other._indices[i])))
                    return true;
            }

            return false;
        }

        /// <inheritdoc cref="HasAny(GameplayTagContainer)"/>
        public bool HasAny(ReadOnlyGameplayTagContainer other) => HasAny(other.Source);

        /// <summary>Returns true if this container contains every exact tag in <paramref name="other"/>. Empty other returns true.</summary>
        public bool HasAllExact(GameplayTagContainer other)
        {
            if (other.IsEmpty)
                return true;

            for (var i = 0; i < other._indices.Count; i++)
            {
                if (_indices.BinarySearch(other._indices[i]) < 0)
                    return false;
            }

            return true;
        }

        /// <inheritdoc cref="HasAllExact(GameplayTagContainer)"/>
        public bool HasAllExact(ReadOnlyGameplayTagContainer other) => HasAllExact(other.Source);

        /// <summary>Returns true if this container contains at least one exact tag from <paramref name="other"/>. Empty other returns false.</summary>
        public bool HasAnyExact(GameplayTagContainer other)
        {
            if (other.IsEmpty)
                return false;

            for (var i = 0; i < other._indices.Count; i++)
            {
                if (_indices.BinarySearch(other._indices[i]) >= 0)
                    return true;
            }

            return false;
        }

        /// <inheritdoc cref="HasAnyExact(GameplayTagContainer)"/>
        public bool HasAnyExact(ReadOnlyGameplayTagContainer other) => HasAnyExact(other.Source);

        public override string ToString()
        {
            if (_indices.Count == 0)
                return "[]";

            var sb = new StringBuilder("[");
            for (var i = 0; i < _indices.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(new GameplayTag(_indices[i]).ToString());
            }
            sb.Append(']');
            return sb.ToString();
        }

        public Enumerator GetEnumerator() => new(this);

        IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Zero-alloc enumerator for <see cref="GameplayTagContainer"/>.
        /// Throws <see cref="System.InvalidOperationException"/> if the container is mutated during iteration.
        /// </summary>
        public struct Enumerator : IEnumerator<GameplayTag>
        {
            private readonly GameplayTagContainer _owner;
            private readonly int _version;
            private int _position;

            internal Enumerator(GameplayTagContainer owner)
            {
                _owner = owner;
                _version = owner._version;
                _position = -1;
            }

            public GameplayTag Current
            {
                get
                {
                    CheckVersion();
                    return new GameplayTag(_owner._indices[_position]);
                }
            }

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                CheckVersion();
                _position++;
                return _position < _owner._indices.Count;
            }

            public void Reset()
            {
                CheckVersion();
                _position = -1;
            }

            public void Dispose() { }

            private void CheckVersion()
            {
                if (_owner._version != _version)
                    throw new System.InvalidOperationException(
                        "GameplayTagContainer was modified during enumeration.");
            }
        }
    }
}
