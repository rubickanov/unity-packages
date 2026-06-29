using System;
using UnityEngine;

namespace Rubickanov.Motor.Modules
{
    /// <summary>
    /// Jump with coyote time and input buffering for responsive feel.
    /// </summary>
    [Serializable]
    public class JumpModule : MotorModuleBase, IStatefulModule
    {
        [SerializeField] private float _jumpForce = 7.5f;
        [SerializeField] private float _coyoteTime = 0.12f;
        [SerializeField] private float _jumpBufferTime = 0.1f;

        public override int Priority => 10;

        private float _coyoteTimer;
        private float _bufferTimer;
        private bool _hasJumped;
        private bool _wasGrounded;
        private Vector3 _prevVelocity;

        /// <summary>Fired when a jump is executed.</summary>
        public event Action<float>? Jumped;

        /// <summary>Fired on landing. Passes the velocity at impact.</summary>
        public event Action<Vector3>? Landed;

        public override void Simulate(float deltaTime)
        {
            // Buffer and timer management
            if (State.JumpPressed)
                _bufferTimer = _jumpBufferTime;

            if (_bufferTimer > 0f) _bufferTimer -= deltaTime;
            if (_coyoteTimer > 0f) _coyoteTimer -= deltaTime;

            // Landing detection — use previous frame velocity (pre-impact)
            if (!_wasGrounded && State.IsGrounded)
            {
                Landed?.Invoke(_prevVelocity);
            }
            _wasGrounded = State.IsGrounded;

            // Cache velocity for next frame's landing check
            _prevVelocity = State.CurrentVelocity;

            if (State.IsGrounded)
            {
                _coyoteTimer = _coyoteTime;
                _hasJumped = false;
            }

            bool canJump = _coyoteTimer > 0f && !_hasJumped;
            bool wantsJump = _bufferTimer > 0f;

            if (canJump && wantsJump)
            {
                // Counteract current vertical velocity + apply jump force in one impulse
                float verticalImpulse = _jumpForce - Body.Velocity.y;
                Body.AddForce(Vector3.up * verticalImpulse, ForceMode.VelocityChange);

                _hasJumped = true;
                _bufferTimer = 0f;
                _coyoteTimer = 0f;

                Jumped?.Invoke(_jumpForce);
            }
        }

        public void SaveState(ref ModuleStateWriter writer)
        {
            writer.Write(_coyoteTimer);
            writer.Write(_bufferTimer);
            writer.Write(_hasJumped);
            writer.Write(_wasGrounded);
            writer.Write(_prevVelocity);
        }

        public void RestoreState(ref ModuleStateReader reader)
        {
            _coyoteTimer = reader.ReadFloat();
            _bufferTimer = reader.ReadFloat();
            _hasJumped = reader.ReadBool();
            _wasGrounded = reader.ReadBool();
            _prevVelocity = reader.ReadVector3();
        }
    }
}
