using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// Key chords bound to console commands. Only the data: <see cref="CommandBindings"/> watches the keyboard and runs
    /// them, so the bindings can be read and changed without a scene. One per <see cref="CommandRegistry"/>, as its
    /// <see cref="CommandRegistry.Bindings"/>, saved with the aliases in <c>config.cfg</c>.
    /// </summary>
    public sealed class BindingRegistry
    {
        private readonly Dictionary<KeyChord, string> _bindings = new();
        private readonly Action _save;

        /// <summary>All bindings.</summary>
        public IReadOnlyDictionary<KeyChord, string> Bindings => _bindings;

        internal BindingRegistry(Action save)
        {
            _save = save;
            ConsoleConfig.ReadBindings(_bindings);
        }

        /// <summary>Binds <paramref name="chord"/> to <paramref name="command"/>, replacing what it was bound to.</summary>
        public void Set(KeyChord chord, string command)
        {
            _bindings[chord] = command;
            Save();
        }

        /// <summary>Removes a binding. Returns true if it existed.</summary>
        public bool Remove(KeyChord chord)
        {
            var removed = _bindings.Remove(chord);
            if (removed) Save();
            return removed;
        }

        /// <summary>Removes all bindings.</summary>
        public void Clear()
        {
            _bindings.Clear();
            Save();
        }

        private void Save() => _save();
    }
}
