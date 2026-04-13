using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// MonoBehaviour that drives a collection of <see cref="ITickable"/>s once
    /// per <c>Update</c>. One runner per entity is the intended wiring (place
    /// it on the same GameObject as <see cref="MonoEntity"/>); a headless
    /// simulation replaces the runner with its own loop and reuses the
    /// <see cref="ITickable"/> implementations unchanged.
    /// </summary>
    public sealed class EntityTickRunner : MonoBehaviour
    {
        private readonly List<ITickable> _tickables = new();
        // Reused scratch buffer so a tickable that registers or unregisters a
        // sibling mid-Tick doesn't corrupt iteration. Reusing _scratch avoids
        // the per-frame allocation that a fresh ToArray() would introduce.
        private readonly List<ITickable> _scratch = new();

        /// <summary>
        /// Registers <paramref name="tickable"/> so it receives <see cref="ITickable.Tick"/>
        /// every frame until <see cref="Unregister"/> is called or the runner
        /// is destroyed. Duplicate registrations are silently ignored so
        /// callers don't need to track their own "already-added" flag.
        /// </summary>
        public void Register(ITickable tickable)
        {
            if (tickable == null) return;
            if (_tickables.Contains(tickable)) return;
            _tickables.Add(tickable);
        }

        /// <summary>
        /// Removes <paramref name="tickable"/>. Safe to call for an item that
        /// was never registered.
        /// </summary>
        public void Unregister(ITickable tickable)
        {
            if (tickable == null) return;
            _tickables.Remove(tickable);
        }

        private void Update()
        {
            // Snapshot into _scratch so mutations of _tickables during Tick —
            // e.g. a tickable unregistering itself after its work, or spawning
            // a sibling — do not corrupt iteration for the current frame.
            // A freshly registered tickable is picked up next frame.
            _scratch.Clear();
            _scratch.AddRange(_tickables);

            float dt = Time.deltaTime;
            for (int i = 0; i < _scratch.Count; i++)
                _scratch[i].Tick(dt);
        }
    }
}
