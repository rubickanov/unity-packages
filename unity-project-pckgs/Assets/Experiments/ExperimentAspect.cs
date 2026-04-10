using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Netcode;
using UnityEngine;

namespace Experiments
{
    public class ExperimentAspect : IEntityAspect
    {
        // Owner-auth + interpolated: owner moves, others see smooth lerp
        [ReplicatedState(Authority = AuthorityMode.Owner, Interpolation = InterpolationMode.Linear)]
        public readonly ReactiveProperty<Vector3> Position = new(Vector3.zero);

        // Server-auth: only server writes health
        [ReplicatedState(Authority = AuthorityMode.Server)]
        public readonly ReactiveProperty<float> Health = new(100f);

        // Server-auth + interpolated: server rotates, clients see smooth rotation
        [ReplicatedState(Authority = AuthorityMode.Server, Interpolation = InterpolationMode.Linear)]
        public readonly ReactiveProperty<Quaternion> Rotation = new(Quaternion.identity);

        // Server event: broadcast when damage happens
        [ReplicatedEvent]
        public readonly Subject<float> DamageDealt = new();

        // Owner event (unreliable): cosmetic footstep
        [ReplicatedEvent(Authority = AuthorityMode.Owner, Reliability = Reliability.Unreliable)]
        public readonly Subject<int> Footstep = new();

        // Non-replicated: local-only UI state
        public readonly ReactiveProperty<bool> IsSelected = new(false);
    }
}
