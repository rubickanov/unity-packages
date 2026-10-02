using System;

namespace Rubickanov.UI
{
    /// <summary>
    /// An item of a <see cref="MenuModel"/>: its text, whether it can be picked, what picking it does and how that
    /// sounds. A game derives from it to carry what its menu draws besides the text.
    /// </summary>
    public class MenuEntry
    {
        private readonly Action? _chosen;

        public MenuEntry(string text, Action? chosen, bool enabled = true)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            _chosen = chosen;
            Enabled = enabled;
        }

        public string Text { get; }

        public bool Enabled { get; }

        /// <summary>Cued when the item is picked; null for silence. Default: <see cref="MenuCue.Select"/>.</summary>
        public MenuCue? Cue { get; private set; } = MenuCue.Select;

        /// <summary>
        /// Sets the cue of picking it: <see cref="MenuCue.Back"/> for an item that changes nothing (STAY), null for
        /// silence.
        /// </summary>
        public MenuEntry WithCue(MenuCue? cue)
        {
            Cue = cue;
            return this;
        }

        internal void Choose() => _chosen?.Invoke();
    }
}
