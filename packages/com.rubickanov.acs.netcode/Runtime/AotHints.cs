using R3;
using UnityEngine;
using UnityEngine.Scripting;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Explicit AOT generic instantiation hints for IL2CPP.
    /// This class is never called at runtime — it exists solely to ensure IL2CPP
    /// generates native code for common generic binding specializations.
    /// For custom unmanaged structs, users must add a link.xml entry.
    /// </summary>
    [Preserve]
    internal static class AotHints
    {
        [Preserve]
        private static void UsedOnlyForAOTCodeGeneration()
        {
            // ReplicatedFieldBinding<T>
            new ReplicatedFieldBinding<int>(default!);
            new ReplicatedFieldBinding<float>(default!);
            new ReplicatedFieldBinding<bool>(default!);
            new ReplicatedFieldBinding<Vector2>(default!);
            new ReplicatedFieldBinding<Vector3>(default!);
            new ReplicatedFieldBinding<Vector4>(default!);
            new ReplicatedFieldBinding<Quaternion>(default!);
            new ReplicatedFieldBinding<Color>(default!);

            // InterpolatedFieldBinding<T> — types with registered lerpers
            new InterpolatedFieldBinding<float>(default!, default!);
            new InterpolatedFieldBinding<double>(default!, default!);
            new InterpolatedFieldBinding<Vector2>(default!, default!);
            new InterpolatedFieldBinding<Vector3>(default!, default!);
            new InterpolatedFieldBinding<Vector4>(default!, default!);
            new InterpolatedFieldBinding<Quaternion>(default!, default!);
            new InterpolatedFieldBinding<Color>(default!, default!);

            // ReplicatedEventBinding<T>
            new ReplicatedEventBinding<int>(default!, default, default);
            new ReplicatedEventBinding<float>(default!, default, default);
            new ReplicatedEventBinding<bool>(default!, default, default);
        }
    }
}
