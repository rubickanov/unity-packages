using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Netcode;
using UnityEngine;

namespace Experiments
{
    /// <summary>
    /// Owner-only: WASD movement. Writes to owner-auth Position.
    /// Other peers see the movement via replication (with interpolation).
    /// </summary>
    [NetworkScope(NetworkScope.OwnerOnly)]
    public class OwnerInputMover : EntityNetworkComponent
    {
        [SerializeField] private float moveSpeed = 5f;

        [Aspect] private ExperimentAspect _aspect = default!;

        private int _footstepCounter;
        private float _footstepTimer;

        private void Update()
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");

            if (Mathf.Abs(h) < 0.01f && Mathf.Abs(v) < 0.01f) return;

            var delta = new Vector3(h, 0f, v) * (moveSpeed * Time.deltaTime);
            _aspect.Position.Value += delta;

            // Fire a cosmetic footstep event every 0.5s while moving
            _footstepTimer += Time.deltaTime;
            if (_footstepTimer >= 0.5f)
            {
                _footstepTimer = 0f;
                _aspect.Footstep.OnNext(++_footstepCounter);
            }
        }
    }
}
