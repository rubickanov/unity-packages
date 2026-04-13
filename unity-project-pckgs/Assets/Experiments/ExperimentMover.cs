using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Netcode;
using UnityEngine;

namespace Experiments
{
    /// <summary>
    /// Integrates <see cref="ExperimentInputCommand.Move"/> into
    /// <see cref="ExperimentAspect.Position"/> once per network tick. Runs on
    /// both the owner (local prediction) and the server (authority); which
    /// write counts as authoritative is decided by
    /// <c>[Replicated(Authority = ...)]</c> on the Position field:
    /// <list type="bullet">
    /// <item><c>Authority = Server</c> — server's Simulate writes the
    /// authoritative value, owner's Simulate is prediction (reconciled against
    /// server state if Position is also <c>Predicted = true</c>).</item>
    /// <item><c>Authority = Owner</c> — owner's Simulate is the authoritative
    /// write, relayed to other peers via the owner-auth path.</item>
    /// </list>
    /// Flip the attribute on the aspect field, no changes needed here.
    /// </summary>
    public class ExperimentMover : EntityNetworkComponent, ISimulate<ExperimentInputCommand>
    {
        [SerializeField] private float moveSpeed = 5f;

        [Aspect] private ExperimentAspect _aspect = default!;

        public void Simulate(in ExperimentInputCommand input, float dt)
        {
            var delta = new Vector3(input.Move.x, 0f, input.Move.y) * (moveSpeed * dt);
            _aspect.Position.Value += delta;
        }
    }
}
