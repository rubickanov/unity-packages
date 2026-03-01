using System;
using UnityEngine;

namespace Rubickanov.Motor.Modules
{
    /// <summary>
    /// Applies a downward sliding force when standing on slopes steeper than
    /// <see cref="_slideAngle"/>. At extreme angles (≥ <see cref="_hardSlideAngle"/>)
    /// the module overrides default physics entirely.
    /// </summary>
    [Serializable]
    public class SlopeSlideModule : MotorModuleBase
    {
        [SerializeField] private float _slideAngle = 46f;
        [SerializeField] private float _slideAcceleration = 15f;
        [SerializeField] private float _hardSlideAngle = 70f;

        public override int Priority => 25;

        public override void Simulate(float deltaTime)
        {
            if (!State.IsGrounded || State.GroundAngle <= _slideAngle)
                return;

            State.IsSliding = true;

            Vector3 slideDir = Vector3.ProjectOnPlane(Vector3.down, State.GroundNormal).normalized;
            float scale = Mathf.InverseLerp(_slideAngle, 90f, State.GroundAngle);
            Body.AddForce(slideDir * (_slideAcceleration * scale), ForceMode.Acceleration);

            if (State.GroundAngle >= _hardSlideAngle)
                State.SkipDefaultPhysics = true;
        }
    }
}
