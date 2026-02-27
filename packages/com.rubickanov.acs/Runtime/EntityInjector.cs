using System;
using UnityEngine;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Hook point for DI frameworks. Set <see cref="Inject"/> to a delegate that injects
    /// dependencies into the given GameObject (e.g. VContainer).
    /// </summary>
    public static class EntityInjector
    {
        public static Action<GameObject>? Inject;
    }
}
