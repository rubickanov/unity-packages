using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Non-generic dispatcher for <see cref="SingletonMonoEntity{T}"/> play-start resets.
    /// Unity refuses <c>[RuntimeInitializeOnLoadMethod]</c> on methods declared inside an
    /// open generic class, so the hook hangs here and fans out to each closed generic base.
    /// <para/>
    /// Each <see cref="SingletonMonoEntity{T}"/> registers its own reset delegate the first
    /// time one of its instances awakes, so this list holds exactly the singleton types the
    /// game actually uses — no type discovery, no reflection.
    /// <para/>
    /// <b>Why not a type scan.</b> This used to walk
    /// <see cref="AppDomain.GetAssemblies"/> → <c>Assembly.GetTypes()</c> at
    /// <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/> to find every concrete
    /// subclass. That materialises every <see cref="Type"/> in every loaded assembly before
    /// the first frame, on every launch, in player builds too — and its whole purpose (a
    /// stale static surviving into the next play session) can only happen in the Editor with
    /// Domain Reload disabled. Self-registration costs one delegate per singleton type and
    /// runs the reset correctly everywhere, including when the Unity runtime is restarted
    /// in-process (Unity as a Library), where an Editor-only guard would have skipped it.
    /// </summary>
    internal static class SingletonMonoEntityResetter
    {
        // Deliberately not cleared by the play-start hook: this list and the per-type
        // `_resetHookRegistered` flags in SingletonMonoEntity<T> share the same static
        // lifetime, so clearing one without the other would leave later sessions unreset.
        private static readonly List<Action> Resetters = new();

        /// <summary>
        /// Registers a per-closed-type reset. Called once per <typeparamref name="T"/> from
        /// <see cref="SingletonMonoEntity{T}"/>'s first <c>Awake</c>; the caller guards
        /// against duplicate registration.
        /// </summary>
        internal static void Register(Action reset)
        {
            Resetters.Add(reset);
        }

        /// <summary>
        /// Nulls the <c>Instance</c> slot of every singleton type registered so far.
        /// Internal rather than private so edit-mode tests can drive it directly.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetAllOnPlayStart()
        {
            for (int i = 0; i < Resetters.Count; i++)
                Resetters[i].Invoke();
        }

        /// <summary>
        /// Test-only observation hook. Read-only on purpose — there is no matching "clear",
        /// because dropping registrations without also clearing every type's
        /// <c>_resetHookRegistered</c> flag would leave those types silently unreset.
        /// Tests assert on the delta across an action, never on the absolute value.
        /// </summary>
        internal static int RegisteredCountForTests => Resetters.Count;
    }
}
