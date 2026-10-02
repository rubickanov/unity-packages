using System;

namespace Rubickanov.UI
{
    /// <summary>
    /// What a menu did, for its sound (<see cref="MenuNavigation.Cues"/>). The four here are what every menu does; a game
    /// adds its own as static instances (<c>public static readonly MenuCue Tape = new("tape")</c>) and compares by
    /// reference.
    /// </summary>
    public sealed class MenuCue
    {
        /// <summary>The focus moved to another item.</summary>
        public static readonly MenuCue Focus = new("focus");

        /// <summary>An item was picked.</summary>
        public static readonly MenuCue Select = new("select");

        /// <summary>A screen or a question went back.</summary>
        public static readonly MenuCue Back = new("back");

        /// <summary>Something that is off was picked.</summary>
        public static readonly MenuCue Denied = new("denied");

        public MenuCue(string name) => Name = name ?? throw new ArgumentNullException(nameof(name));

        public string Name { get; }

        public override string ToString() => Name;
    }
}
