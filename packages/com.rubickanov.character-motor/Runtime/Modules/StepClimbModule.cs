using System;
using UnityEngine;

namespace Rubickanov.Motor.Modules
{
    /// <summary>
    /// Smoothly climbs small steps/obstacles. Casts a ray at step height to check
    /// if there's a walkable surface above the obstacle.
    /// </summary>
    [Serializable]
    public class StepClimbModule : MotorModuleBase, IStatefulModule
    {
        [SerializeField] private float _maxStepHeight = 0.35f;
        [SerializeField] private float _stepCheckDepth = 0.4f;
        [SerializeField] private float _stepClimbSpeed = 5f;
        [SerializeField] private float _maxSlopeAngle = 46f;

        public override int Priority => 20;

        private float _pendingStepUp;
        private float _initialCapsuleHeight;

        protected override void OnInitialize()
        {
            _initialCapsuleHeight = Body.CapsuleHeight;
        }

        public override void Simulate(float deltaTime)
        {
            // Continue pending climb
            if (_pendingStepUp > 0f)
            {
                float lift = Mathf.Min(_pendingStepUp, _stepClimbSpeed * deltaTime);
                Vector3 pos = Body.Position;
                pos.y += lift;
                Body.MovePosition(pos);
                _pendingStepUp -= lift;
                return;
            }

            // Only climb when grounded and moving
            if (!State.IsGrounded) return;
            if (State.MoveInput.sqrMagnitude < 0.01f) return;

            Vector3 moveDir = State.DesiredVelocity.normalized;
            if (moveDir.sqrMagnitude < 0.01f) return;

            float maxStep = _maxStepHeight * (Body.CapsuleHeight / _initialCapsuleHeight);
            float checkDepth = _stepCheckDepth;
            Vector3 feetPos = Body.Position;

            // 1. Cast forward at feet level — is there an obstacle?
            bool hitLow = Body.Raycast(
                feetPos + Vector3.up * 0.05f,
                moveDir,
                checkDepth,
                out RaycastHit lowHit);

            if (!hitLow) return;

            // 2. Cast forward at step height — is it clear above the step?
            bool hitHigh = Body.Raycast(
                feetPos + Vector3.up * (maxStep + 0.05f),
                moveDir,
                checkDepth,
                out _);

            if (hitHigh) return; // Wall, not a step

            // 3. Cast down from above to find the step surface
            bool foundSurface = Body.SphereCast(
                feetPos + Vector3.up * (maxStep + 0.1f) + moveDir * checkDepth,
                0.1f,
                Vector3.down,
                maxStep,
                out RaycastHit stepHit);

            if (!foundSurface) return;

            // Check slope of step surface
            float stepAngle = Vector3.Angle(Vector3.up, stepHit.normal);
            if (stepAngle > _maxSlopeAngle) return;

            // 4. Start smooth climb
            float stepUp = stepHit.point.y - feetPos.y;
            if (stepUp > 0.01f && stepUp <= maxStep)
            {
                _pendingStepUp = stepUp + 0.01f;
            }
        }

        public void SaveState(ref ModuleStateWriter writer)
        {
            writer.Write(_pendingStepUp);
            writer.Write(_initialCapsuleHeight);
        }

        public void RestoreState(ref ModuleStateReader reader)
        {
            _pendingStepUp = reader.ReadFloat();
            _initialCapsuleHeight = reader.ReadFloat();
        }
    }
}
