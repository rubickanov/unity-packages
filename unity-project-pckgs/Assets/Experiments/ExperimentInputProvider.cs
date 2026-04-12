using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Netcode;
using UnityEngine;

namespace Experiments
{
    /// <summary>
    /// Owner-only: gathers WASD each tick for the prediction pipeline and fires
    /// a cosmetic Footstep event every 0.5 s while moving. Movement itself is
    /// done by <see cref="ExperimentMover"/> via <see cref="ISimulate{TInput}"/>.
    /// </summary>
    /// <remarks>
    /// The split between provider and mover lets a single prefab work in either
    /// authority mode. Flip <c>ExperimentAspect.Position</c> to
    /// <c>Authority = Server</c> and the owner's local Simulate becomes pure
    /// prediction (reconciled against server state); flip it to
    /// <c>Authority = Owner</c> and the owner's write is the authoritative one
    /// that gets relayed to other peers. The provider and mover components do
    /// not change.
    /// </remarks>
    [NetworkScope(NetworkScope.OwnerOnly)]
    public class ExperimentInputProvider : EntityNetworkComponent, IInputProvider<ExperimentInputCommand>
    {
        [Aspect] private ExperimentAspect _aspect = default!;

        private int _footstepCounter;
        private float _footstepTimer;

        public ExperimentInputCommand Gather()
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            return new ExperimentInputCommand { Move = new Vector2(h, v) };
        }

        private void Update()
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            bool moving = Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f;
            if (!moving)
            {
                _footstepTimer = 0f;
                return;
            }

            _footstepTimer += Time.deltaTime;
            if (_footstepTimer >= 0.5f)
            {
                _footstepTimer = 0f;
                _aspect.Footstep.OnNext(++_footstepCounter);
            }
        }
    }
}
