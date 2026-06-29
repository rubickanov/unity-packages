using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Single-scene driver that ticks every registered <see cref="ISmoothBinding"/> once per
    /// rendered frame in <c>LateUpdate</c>. LateUpdate is chosen deliberately: interpolated
    /// bindings produce their smoothed value from <c>EntityReplicator.Update()</c>'s
    /// <c>TickRender</c> call, so reading <c>.Smooth()</c> any time after that within the
    /// same frame returns a fresh value.
    /// <para>
    /// Bindings are registered lazily — the backing <see cref="SmoothDriverHost"/>
    /// <see cref="MonoBehaviour"/> is created on first Register call in play mode, and
    /// persists via <see cref="Object.DontDestroyOnLoad"/> so the driver survives scene
    /// transitions. Callers are still responsible for disposing their bindings; the driver
    /// does not automatically clean up bindings whose setters point at destroyed objects.
    /// </para>
    /// </summary>
    internal static class SmoothDriver
    {
        private static readonly List<ISmoothBinding> s_Bindings = new();
        private static readonly List<ISmoothBinding> s_PendingRemoval = new();
        private static bool s_Iterating;
        private static SmoothDriverHost? s_Host;

        // With Domain Reload disabled the static list would retain bindings captured in a
        // previous play session and tick their dangling setters (pointing at destroyed
        // Unity objects). SubsystemRegistration clears both the list and the host
        // reference — the old host GameObject is destroyed by Unity at play exit, so the
        // stale reference is already fake-null; nulling it out makes EnsureHost() create
        // a fresh one on the next Register call.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Bindings.Clear();
            s_PendingRemoval.Clear();
            s_Iterating = false;
            s_Host = null;
        }

        internal static void Register(ISmoothBinding binding)
        {
            s_Bindings.Add(binding);
            EnsureHost();
        }

        internal static void Unregister(ISmoothBinding binding)
        {
            // Removing from s_Bindings during TickAll iteration would shift indices and
            // cause the next sibling to be skipped. Defer the removal — SmoothBinding.Tick
            // guards against its own post-Dispose invocation, so a still-listed disposed
            // binding is a no-op until the iterator drains.
            if (s_Iterating)
                s_PendingRemoval.Add(binding);
            else
                s_Bindings.Remove(binding);
        }

        /// <summary>
        /// Drives every registered binding once. Normally invoked from the hidden host's
        /// <c>LateUpdate</c>; exposed as <c>internal</c> so edit-mode tests can tick
        /// without instantiating a <see cref="MonoBehaviour"/>.
        /// </summary>
        internal static void TickAll()
        {
            s_Iterating = true;
            try
            {
                for (int i = 0; i < s_Bindings.Count; i++)
                    s_Bindings[i].Tick();
            }
            finally
            {
                s_Iterating = false;
                if (s_PendingRemoval.Count > 0)
                {
                    for (int i = 0; i < s_PendingRemoval.Count; i++)
                        s_Bindings.Remove(s_PendingRemoval[i]);
                    s_PendingRemoval.Clear();
                }
            }
        }

        private static void EnsureHost()
        {
            if (s_Host != null) return;
            if (!Application.isPlaying) return;

            var go = new GameObject("[ACS.Netcode.SmoothDriver]")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Object.DontDestroyOnLoad(go);
            s_Host = go.AddComponent<SmoothDriverHost>();
        }

        private sealed class SmoothDriverHost : MonoBehaviour
        {
            private void LateUpdate() => TickAll();
        }
    }
}
