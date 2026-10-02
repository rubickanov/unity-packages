using Rubickanov.ACS.Runtime;
using Rubickanov.ACS.Runtime.Netcode;
using UnityEngine;

namespace Experiments
{
    /// <summary>
    /// Server-only: rotates the entity and deals periodic damage.
    /// Demonstrates server-auth state + server-auth events.
    /// </summary>
    [NetworkScope(NetworkScope.ServerOnly)]
    public class ServerLogic : EntityNetworkComponent
    {
        [SerializeField] private float rotationSpeed = 45f;
        [SerializeField] private float damageInterval = 3f;
        [SerializeField] private float damageAmount = 10f;

        [Aspect] private ExperimentAspect _aspect = default!;

        private float _damageTimer;

        private void Update()
        {
            // Rotate the entity (server-auth, clients see interpolated)
            var rot = _aspect.Rotation.Value * Quaternion.Euler(0f, rotationSpeed * Time.deltaTime, 0f);
            _aspect.Rotation.Value = rot;

            // Periodic damage
            _damageTimer += Time.deltaTime;
            if (_damageTimer >= damageInterval)
            {
                _damageTimer = 0f;
                _aspect.Health.Value = Mathf.Max(0f, _aspect.Health.Value - damageAmount);
                _aspect.DamageDealt.OnNext(damageAmount);
                Debug.Log($"[Server] Dealt {damageAmount} damage. Health: {_aspect.Health.Value}");
            }
        }
    }
}
