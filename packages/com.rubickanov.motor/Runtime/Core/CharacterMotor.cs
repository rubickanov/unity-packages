using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Thin MonoBehaviour shell that bridges Unity lifecycle to <see cref="MotorSimulation"/>.
    /// In singleplayer, auto-ticks the simulation in FixedUpdate.
    /// In multiplayer, set <see cref="AutoSimulate"/> to false and call <see cref="Simulate"/> manually.
    /// </summary>
    [RequireComponent(typeof(CapsuleCollider))]
    public class CharacterMotor : MonoBehaviour, IForceReceiver
    {
        // ══════════════════════════════════════════════════════════
        //  Serialized
        // ══════════════════════════════════════════════════════════

        [SerializeField] private MotorBodyType _bodyType = MotorBodyType.Kinematic;
        [SerializeField] private LayerMask _groundMask = ~0;

        [Header("Modules")]
        [SerializeReference] private List<IMotorModule> _modules = new();

        // ══════════════════════════════════════════════════════════
        //  Private state
        // ══════════════════════════════════════════════════════════

        private MotorSimulation _simulation = default!;
        private IMotorBody _body = default!;
        private IMotorInputProvider? _inputProvider;

        // Speed modifiers from external sources
        private readonly Dictionary<object, float> _speedModifiers = new();

        // External force accumulator (applied between ticks)
        private Vector3 _pendingExternalForce;

        // Input buffering for single-frame pulses across Update → FixedUpdate
        private bool _jumpBuffered;
        private bool _crouchBuffered;

        // ══════════════════════════════════════════════════════════
        //  Public API
        // ══════════════════════════════════════════════════════════

        /// <summary>The underlying simulation. Use for manual ticking, SaveState, etc.</summary>
        public MotorSimulation Simulation => _simulation;

        /// <summary>Read-only access to the motor state.</summary>
        public IReadOnlyMotorState State => _simulation.State;

        /// <summary>The physics body.</summary>
        public IMotorBody Body => _body;

        /// <summary>
        /// Auto-tick in FixedUpdate. Set to false for manual ticking (multiplayer).
        /// </summary>
        public bool AutoSimulate { get; set; } = true;

        /// <summary>Fires every simulation tick with an immutable state snapshot.</summary>
        public event Action<MotorSnapshot>? StateUpdated
        {
            add => _simulation.StateUpdated += value;
            remove => _simulation.StateUpdated -= value;
        }

        /// <summary>
        /// Set the input provider for auto-tick mode.
        /// </summary>
        public void SetInputProvider(IMotorInputProvider provider)
        {
            _inputProvider = provider;
        }

        public void ClearInputProvider()
        {
            _inputProvider = null;
        }

        /// <summary>Get a module by type.</summary>
        public T? GetModule<T>() where T : class, IMotorModule
            => _simulation.GetModule<T>();

        /// <summary>
        /// Manual simulation tick. Set <see cref="AutoSimulate"/> to false first.
        /// </summary>
        public void Simulate(MotorInput input, float deltaTime)
        {
            FlushExternalState();
            _simulation.Simulate(input, deltaTime);
        }

        // ══════════════════════════════════════════════════════════
        //  IForceReceiver
        // ══════════════════════════════════════════════════════════

        public void AddExternalForce(Vector3 force)
        {
            _pendingExternalForce += force;
        }

        public void SetSpeedModifier(object source, float multiplier)
        {
            _speedModifiers[source] = multiplier;
        }

        public void RemoveSpeedModifier(object source)
        {
            _speedModifiers.Remove(source);
        }

        // ══════════════════════════════════════════════════════════
        //  Unity lifecycle
        // ══════════════════════════════════════════════════════════

        private void Awake()
        {
            var capsule = GetComponent<CapsuleCollider>();

            if (!TryGetComponent(out Rigidbody rb))
                rb = gameObject.AddComponent<Rigidbody>();

            rb.freezeRotation = true;

            _body = _bodyType switch
            {
                MotorBodyType.Rigidbody => new RigidbodyMotorBody(rb, capsule, _groundMask),
                MotorBodyType.Kinematic => new KinematicMotorBody(rb, capsule, _groundMask),
                _ => throw new ArgumentOutOfRangeException()
            };

            _simulation = new MotorSimulation(_body, _modules);
        }

        private void Update()
        {
            // Buffer single-frame pulses (captured in Update, consumed in FixedUpdate)
            if (_inputProvider != null)
            {
                if (_inputProvider.JumpPressed) _jumpBuffered = true;
                if (_inputProvider.CrouchPressed) _crouchBuffered = true;
            }

            _simulation.VisualUpdate(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (!AutoSimulate) return;

            FlushExternalState();
            _simulation.Simulate(ReadInput(), Time.fixedDeltaTime);
        }

        // ══════════════════════════════════════════════════════════
        //  Private
        // ══════════════════════════════════════════════════════════

        private MotorInput ReadInput()
        {
            var input = new MotorInput();

            if (_inputProvider != null)
            {
                input.Move = _inputProvider.MoveInput;
                input.Sprint = _inputProvider.SprintHeld;
            }

            // Use buffered pulses
            input.Jump = _jumpBuffered;
            input.Crouch = _crouchBuffered;
            _jumpBuffered = false;
            _crouchBuffered = false;

            return input;
        }

        private void FlushExternalState()
        {
            // External force
            if (_pendingExternalForce.sqrMagnitude > 0.001f)
            {
                _simulation.AddPendingForce(_pendingExternalForce);
                _pendingExternalForce = Vector3.zero;
            }

            // Speed modifiers
            if (_speedModifiers.Count > 0)
            {
                float totalMod = 1f;
                foreach (var mod in _speedModifiers.Values)
                    totalMod *= mod;
                _simulation.SetExternalSpeedMultiplier(totalMod);
            }
            else
            {
                _simulation.SetExternalSpeedMultiplier(1f);
            }
        }
    }
}
