using System.Collections;
using System.Collections.Generic;

namespace Rubickanov.GameplayTags
{
    /// <summary>
    /// Read-only view over a <see cref="GameplayTagContainer"/>. Forwards all query operations
    /// without exposing mutation. Intended as the return type for accessors that yield container
    /// state owned by another object (e.g. <see cref="SerializedGameplayTagContainer.Container"/>).
    /// </summary>
    public readonly struct ReadOnlyGameplayTagContainer : IEnumerable<GameplayTag>
    {
        private static readonly GameplayTagContainer EmptyContainer = new();

        private readonly GameplayTagContainer? _source;

        internal GameplayTagContainer Source => _source ?? EmptyContainer;

        public ReadOnlyGameplayTagContainer(GameplayTagContainer source)
        {
            _source = source;
        }

        public static implicit operator ReadOnlyGameplayTagContainer(GameplayTagContainer source)
            => new(source);

        public int Count => Source.Count;

        public bool IsEmpty => Source.IsEmpty;

        public bool HasTag(GameplayTag tag) => Source.HasTag(tag);

        public bool HasTagExact(GameplayTag tag) => Source.HasTagExact(tag);

        public bool HasAll(GameplayTagContainer other) => Source.HasAll(other);

        public bool HasAll(ReadOnlyGameplayTagContainer other) => Source.HasAll(other.Source);

        public bool HasAny(GameplayTagContainer other) => Source.HasAny(other);

        public bool HasAny(ReadOnlyGameplayTagContainer other) => Source.HasAny(other.Source);

        public bool HasAllExact(GameplayTagContainer other) => Source.HasAllExact(other);

        public bool HasAllExact(ReadOnlyGameplayTagContainer other) => Source.HasAllExact(other.Source);

        public bool HasAnyExact(GameplayTagContainer other) => Source.HasAnyExact(other);

        public bool HasAnyExact(ReadOnlyGameplayTagContainer other) => Source.HasAnyExact(other.Source);

        public override string ToString() => Source.ToString();

        public GameplayTagContainer.Enumerator GetEnumerator() => Source.GetEnumerator();

        IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
