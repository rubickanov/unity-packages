using R3;
using Rubickanov.ACS.Runtime;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests.Integration
{
    // ---- Monster aspect (regression #2) -------------------------------------
    //
    // 65 [Replicated] ReactiveProperty<int> fields — exactly one over the
    // 64-field cap. Used to drive the clamp path in AspectReplicator.OnNetworkSpawn
    // through a real spawn, not a reflection-injected stub. The clamp is an
    // inline `if (_bindings.Length > 64)` guard, so the only way to exercise it
    // from integration-level code is to scan a real aspect that exceeds the cap.
    //
    // Why 65 (and not 70 or 100): the point of the test is to verify the
    // guard fires at the boundary. Going further than 65 would only increase
    // test runtime without adding coverage.
    //
    // Field naming is F00..F64 so the alphabetical sort in ReplicationScanner
    // is trivially stable.

    public sealed class MonsterStateAspect : IEntityAspect
    {
        [Replicated] public ReactiveProperty<int> F00 = new(0);
        [Replicated] public ReactiveProperty<int> F01 = new(0);
        [Replicated] public ReactiveProperty<int> F02 = new(0);
        [Replicated] public ReactiveProperty<int> F03 = new(0);
        [Replicated] public ReactiveProperty<int> F04 = new(0);
        [Replicated] public ReactiveProperty<int> F05 = new(0);
        [Replicated] public ReactiveProperty<int> F06 = new(0);
        [Replicated] public ReactiveProperty<int> F07 = new(0);
        [Replicated] public ReactiveProperty<int> F08 = new(0);
        [Replicated] public ReactiveProperty<int> F09 = new(0);
        [Replicated] public ReactiveProperty<int> F10 = new(0);
        [Replicated] public ReactiveProperty<int> F11 = new(0);
        [Replicated] public ReactiveProperty<int> F12 = new(0);
        [Replicated] public ReactiveProperty<int> F13 = new(0);
        [Replicated] public ReactiveProperty<int> F14 = new(0);
        [Replicated] public ReactiveProperty<int> F15 = new(0);
        [Replicated] public ReactiveProperty<int> F16 = new(0);
        [Replicated] public ReactiveProperty<int> F17 = new(0);
        [Replicated] public ReactiveProperty<int> F18 = new(0);
        [Replicated] public ReactiveProperty<int> F19 = new(0);
        [Replicated] public ReactiveProperty<int> F20 = new(0);
        [Replicated] public ReactiveProperty<int> F21 = new(0);
        [Replicated] public ReactiveProperty<int> F22 = new(0);
        [Replicated] public ReactiveProperty<int> F23 = new(0);
        [Replicated] public ReactiveProperty<int> F24 = new(0);
        [Replicated] public ReactiveProperty<int> F25 = new(0);
        [Replicated] public ReactiveProperty<int> F26 = new(0);
        [Replicated] public ReactiveProperty<int> F27 = new(0);
        [Replicated] public ReactiveProperty<int> F28 = new(0);
        [Replicated] public ReactiveProperty<int> F29 = new(0);
        [Replicated] public ReactiveProperty<int> F30 = new(0);
        [Replicated] public ReactiveProperty<int> F31 = new(0);
        [Replicated] public ReactiveProperty<int> F32 = new(0);
        [Replicated] public ReactiveProperty<int> F33 = new(0);
        [Replicated] public ReactiveProperty<int> F34 = new(0);
        [Replicated] public ReactiveProperty<int> F35 = new(0);
        [Replicated] public ReactiveProperty<int> F36 = new(0);
        [Replicated] public ReactiveProperty<int> F37 = new(0);
        [Replicated] public ReactiveProperty<int> F38 = new(0);
        [Replicated] public ReactiveProperty<int> F39 = new(0);
        [Replicated] public ReactiveProperty<int> F40 = new(0);
        [Replicated] public ReactiveProperty<int> F41 = new(0);
        [Replicated] public ReactiveProperty<int> F42 = new(0);
        [Replicated] public ReactiveProperty<int> F43 = new(0);
        [Replicated] public ReactiveProperty<int> F44 = new(0);
        [Replicated] public ReactiveProperty<int> F45 = new(0);
        [Replicated] public ReactiveProperty<int> F46 = new(0);
        [Replicated] public ReactiveProperty<int> F47 = new(0);
        [Replicated] public ReactiveProperty<int> F48 = new(0);
        [Replicated] public ReactiveProperty<int> F49 = new(0);
        [Replicated] public ReactiveProperty<int> F50 = new(0);
        [Replicated] public ReactiveProperty<int> F51 = new(0);
        [Replicated] public ReactiveProperty<int> F52 = new(0);
        [Replicated] public ReactiveProperty<int> F53 = new(0);
        [Replicated] public ReactiveProperty<int> F54 = new(0);
        [Replicated] public ReactiveProperty<int> F55 = new(0);
        [Replicated] public ReactiveProperty<int> F56 = new(0);
        [Replicated] public ReactiveProperty<int> F57 = new(0);
        [Replicated] public ReactiveProperty<int> F58 = new(0);
        [Replicated] public ReactiveProperty<int> F59 = new(0);
        [Replicated] public ReactiveProperty<int> F60 = new(0);
        [Replicated] public ReactiveProperty<int> F61 = new(0);
        [Replicated] public ReactiveProperty<int> F62 = new(0);
        [Replicated] public ReactiveProperty<int> F63 = new(0);
        [Replicated] public ReactiveProperty<int> F64 = new(0);
    }

    /// <summary>Forces <see cref="MonsterStateAspect"/> creation in Awake.</summary>
    public sealed class MonsterStateAspectRegistrar : MonoBehaviour, IEntityComponent
    {
        private void Awake()
        {
            GetComponentInParent<MonoEntity>().Require<MonsterStateAspect>();
        }
    }
}
