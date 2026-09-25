using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// Key chords bound to console commands. Only the data: <see cref="CommandBindings"/> watches the keyboard and runs
    /// them, so the bindings can be read and changed without a scene.
    /// </summary>
    public class BindingRegistry
    {
        private static BindingRegistry? _instance;
        public static BindingRegistry Instance => _instance ??= new BindingRegistry();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => _instance = null;

        private readonly Dictionary<KeyChord, string> _bindings = new();

        /// <summary>All bindings.</summary>
        public IReadOnlyDictionary<KeyChord, string> Bindings => _bindings;

        private BindingRegistry() => ConsoleConfig.ReadBindings(_bindings);

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

        private void Save() => ConsoleConfig.Write(AliasRegistry.Instance, this);
    }
}
