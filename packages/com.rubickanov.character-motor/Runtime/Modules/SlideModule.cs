using System;
using UnityEngine;

namespace Rubickanov.Motor.Modules
{
    /// <summary>
    /// Tactical slide: sprint + crouch triggers a momentum-based slide along the ground.
    /// Interacts with slopes — accelerates downhill, decelerates uphill.
    /// Must run after SprintModule (5) and JumpModule (10), but before CrouchModule (15).
    /// </summary>
    [Serializable]
    public class SlideModule : MotorModuleBase, IStatefulModule
    {
        [Header("Activation")]
        [SerializeField] private float _minSlideSpeed = 4f;
        [SerializeField] private float _slideCooldown = 1f;

        [Header("Speed")]
        [SerializeField] private float _slideBoost = 1.5f;
        [SerializeField] private float _slideFriction = 5f;
        [SerializeField] private float _maxSlideSpeed = 15f;
        [SerializeField] private float _stopSpeed = 2f;

        [Header("Slope")]
        [SerializeField] private float _slopeBoostFactor = 8f;

        [Header("Control")]
        [Range(0f, 1f)]
        [SerializeField] private float _slideControl = 0.15f;
        [SerializeField] private float _slideSteerSpeed = 3f;

        [Header("Height")]
        [SerializeField] private float _standHeight = 2f;
        [SerializeField] private float _slideHeight = 1.2f;
        [SerializeField] private float _heightTransitionSpeed = 10f;
        [SerializeField] private float _ceilingCheckRadius = 0.25f;

        public override int Priority => 12;

        private bool _isSliding;
        private float _cooldownTimer;
        private float _currentHeight;
        private Vector3 _slideDirection;
        private Rigidbody _ownRb = default!;
        private readonly RaycastHit[] _ceilingHits = new RaycastHit[4];

        /// <summary>Camera height offset caused by sliding. Game code can read this to adjust camera position.</summary>
        public float CameraHeightOffset { get; private set; }

        /// <summary>Whether the module is currently in a slide.</summary>
        public bool IsSliding => _isSliding;

        /// <summary>Fired when slide state changes.</summary>
        public event Action<bool>? SlideChanged;

        protected override void OnInitialize()
        {
            _currentHeight = _standHeight;
            _ownRb = Body.Transform.GetComponentInParent<Rigidbody>();
        }

        public override void Simulate(float deltaTime)
        {
            if (_cooldownTimer > 0f)
                _cooldownTimer -= deltaTime;

            if (_isSliding)
                SimulateSlide(deltaTime);
            else
                TryEnterSlide();

            // Recover height after slide ends
            if (!_isSliding && _currentHeight < _standHeight)
            {
                if (CanStand())
                {
                    _currentHeight = Mathf.MoveTowards(_currentHeight, _standHeight, _heightTransitionSpeed * deltaTime);
                    Body.SetCapsuleHeight(_currentHeight);
                }
            }
        }

        public override void VisualUpdate(float deltaTime)
        {
            CameraHeightOffset = _currentHeight - _standHeight;
        }

        private void TryEnterSlide()
        {
            if (!State.CrouchPressed) return;
            if (!State.IsSprinting) return;
            if (!State.IsGrounded) return;
            if (_cooldownTimer > 0f) return;

            Vector3 vel = Body.Velocity;
            Vector3 horizontal = new Vector3(vel.x, 0f, vel.z);
            if (horizontal.magnitude < _minSlideSpeed) return;

            // Enter slide
            _isSliding = true;
            _slideDirection = horizontal.normalized;

            // Consume crouch input so CrouchModule doesn't toggle
            State.CrouchPressed = false;

            // Initial speed boost
            Body.AddForce(_slideDirection * _slideBoost, ForceMode.VelocityChange);

            SlideChanged?.Invoke(true);
        }

        private void SimulateSlide(float deltaTime)
        {
            // Check exit conditions first
            if (CheckSlideExit())
            {
                // Consume crouch so CrouchModule doesn't toggle on the same frame
                State.CrouchPressed = false;
                ExitSlide();
                return;
            }

            // Consume crouch so CrouchModule doesn't interfere
            State.CrouchPressed = false;

            // Set state flags
            State.IsSliding = true;
            State.IsCrouching = true;

            // Height transition
            _currentHeight = Mathf.MoveTowards(_currentHeight, _slideHeight, _heightTransitionSpeed * deltaTime);
            Body.SetCapsuleHeight(_currentHeight);

            // Take over physics
            State.SkipDefaultPhysics = true;

            Vector3 vel = Body.Velocity;
            Vector3 horizontal = new Vector3(vel.x, 0f, vel.z);

            // Friction (deceleration on flat ground)
            if (horizontal.sqrMagnitude > 0.01f)
            {
                Vector3 frictionForce = -horizontal.normalized * _slideFriction;
                Body.AddForce(frictionForce, ForceMode.Acceleration);
            }

            // Player steering during slide
            if (_slideControl > 0f && State.MoveInput.sqrMagnitude > 0.01f)
            {
                Vector3 inputDir = State.DesiredVelocity.sqrMagnitude > 0.01f
                    ? State.DesiredVelocity.normalized
                    : _slideDirection;

                _slideDirection = Vector3.Slerp(_slideDirection, inputDir, _slideControl * _slideSteerSpeed * deltaTime).normalized;

                // Redirect current velocity towards new slide direction
                float speed = horizontal.magnitude;
                Vector3 target = _slideDirection * speed;
                Vector3 steer = (target - horizontal) * _slideControl;
                Body.AddForce(steer, ForceMode.VelocityChange);
            }

            // Slope interaction: project gravity onto ground plane
            if (State.IsGrounded && State.GroundAngle > 1f)
            {
                Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, State.GroundNormal).normalized;
                float slopeDot = Vector3.Dot(slopeDir, _slideDirection);

                // Positive = sliding downhill, negative = sliding uphill
                Body.AddForce(slopeDir * (_slopeBoostFactor * Mathf.Abs(slopeDot)), ForceMode.Acceleration);
            }

            // Clamp horizontal speed
            vel = Body.Velocity;
            horizontal = new Vector3(vel.x, 0f, vel.z);
            if (horizontal.magnitude > _maxSlideSpeed)
            {
                Vector3 clamped = horizontal.normalized * _maxSlideSpeed;
                Body.AddForce(new Vector3(clamped.x - vel.x, 0f, clamped.z - vel.z), ForceMode.VelocityChange);
            }
        }

        private bool CheckSlideExit()
        {
            // Lost ground
            if (!State.IsGrounded)
                return true;

            // Speed too low
            Vector3 vel = Body.Velocity;
            Vector3 horizontal = new Vector3(vel.x, 0f, vel.z);
            if (horizontal.magnitude < _stopSpeed)
                return true;

            // Player pressed crouch again to cancel
            if (State.CrouchPressed)
                return true;

            // Jump cancels slide (JumpModule already ran at priority 10, it will handle the jump)
            if (State.JumpPressed)
                return true;

            return false;
        }

        private void ExitSlide()
        {
            _isSliding = false;
            _cooldownTimer = _slideCooldown;
            SlideChanged?.Invoke(false);
        }

        private bool CanStand()
        {
            float checkDist = _standHeight - _slideHeight;
            Vector3 origin = Body.Position + Vector3.up * _slideHeight;

            int count = Physics.SphereCastNonAlloc(
                origin, _ceilingCheckRadius, Vector3.up, _ceilingHits,
                checkDist, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (_ceilingHits[i].collider.attachedRigidbody != _ownRb)
                    return false;
            }

            return true;
        }

        public void SaveState(ref ModuleStateWriter writer)
        {
            writer.Write(_isSliding);
            writer.Write(_cooldownTimer);
            writer.Write(_currentHeight);
            writer.Write(_slideDirection);
        }

        public void RestoreState(ref ModuleStateReader reader)
        {
            _isSliding = reader.ReadBool();
            _cooldownTimer = reader.ReadFloat();
            _currentHeight = reader.ReadFloat();
            _slideDirection = reader.ReadVector3();
        }
    }
}
