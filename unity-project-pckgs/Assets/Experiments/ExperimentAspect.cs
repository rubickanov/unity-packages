using R3;
using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Netcode;
using UnityEngine;

namespace Experiments
{
    public class ExperimentAspect : IEntityAspect
    {
        // Movement target for the prediction pipeline. Flip Authority and/or
        // [Predicted] here; the input/mover components stay the same.
        //   Authority = Server + [Predicted]
        //       Full prediction: owner predicts locally each tick, server
        //       Simulates as authority, owner reconciles against arriving state.
        //   Authority = Server (no [Predicted])
        //       Owner sends input → server Simulates → broadcasts. Owner's
        //       local Simulate still runs, so each broadcast produces a
        //       visible snap-back — classic "no prediction" behaviour.
        //   Authority = Owner
        //       Owner's Simulate writes are authoritative, relayed to peers
        //       via the owner-auth path. Do NOT combine with [Predicted] —
        //       PredictionScanner drops it with a warning (the owner IS
        //       authority, and running reconcile on self-relayed batches
        //       would replay Simulate a second time per tick and accelerate
        //       the owner visibly).
        [ReplicatedState(Authority = AuthorityMode.Owner, Interpolation = InterpolationMode.Linear)]
        [Predicted]
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
