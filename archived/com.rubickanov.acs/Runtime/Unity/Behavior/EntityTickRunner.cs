using System;
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
        // Rebuild _scratch only when the set actually changed, not every frame.
        private bool _scratchDirty;

        /// <summary>
        /// Registers <paramref name="tickable"/> so it receives <see cref="ITickable.Tick"/>
        /// from the next frame onward until <see cref="Unregister"/> is called
        /// or the runner is destroyed. Tickables registered during a Tick are
        /// not invoked in the current frame — the runner iterates a snapshot
        /// taken at the start of Update. Duplicate registrations are silently
        /// ignored so callers don't need to track their own "already-added" flag.
        /// </summary>
        public void Register(ITickable tickable)
        {
            if (tickable == null) return;
            if (_tickables.Contains(tickable)) return;
            _tickables.Add(tickable);
            _scratchDirty = true;
        }

        /// <summary>
        /// Removes <paramref name="tickable"/> so it no longer receives
        /// <see cref="ITickable.Tick"/> from the next frame onward. Safe to
        /// call for an item that was never registered. If invoked during a
        /// <see cref="ITickable.Tick"/> (by the tickable itself or a sibling),
        /// the removed tickable may still receive the current frame's Tick —
        /// the runner iterates a snapshot taken at the start of Update.
        /// </summary>
        public void Unregister(ITickable tickable)
        {
            if (tickable == null) return;
            if (_tickables.Remove(tickable))
                _scratchDirty = true;
        }

        private void Update()
        {
            // Snapshot into _scratch so mutations of _tickables during Tick —
            // e.g. a tickable unregistering itself after its work, or spawning
            // a sibling — do not corrupt iteration for the current frame.
            // A freshly registered tickable is picked up next frame.
            // Rebuild only when the set changed since the last snapshot; otherwise
            // _scratch already equals _tickables, so skip the per-frame copy. Cleared
            // before iterating, so a mutation during Tick re-dirties for next frame.
            if (_scratchDirty)
            {
                _scratch.Clear();
                _scratch.AddRange(_tickables);
                _scratchDirty = false;
            }

            float dt = Time.deltaTime;
            for (int i = 0; i < _scratch.Count; i++)
            {
                // Isolate each tickable. Without the try/catch a single throwing Tick skips
                // every subsequent tickable for the current frame — an AI entity drops a
                // target, a cooldown fails to decrement, all because an unrelated sibling
                // threw. Runner is an aggregator by design (see class docstring), so loud-
                // and-continue beats fail-fast here.
                try { _scratch[i].Tick(dt); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }
    }
}
