using UnityEngine;

namespace Rubickanov.GameplayTags
{
    internal static class GameplayTagsRuntimeInit
    {
        // Reset the installed registry on Play Mode enter so stale state from a previous
        // session doesn't survive when Enter Play Mode > Reload Domain is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            GameplayTagRegistry.Uninstall();
        }
    }
}
