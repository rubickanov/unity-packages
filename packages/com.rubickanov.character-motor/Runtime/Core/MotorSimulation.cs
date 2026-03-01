using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Pure C# simulation runner. Holds state, modules, and body reference.
    /// Can be ticked from FixedUpdate (singleplayer) or from a network tick system (multiplayer).
    /// </summary>
    public class MotorSimulation : IModuleResolver
    {
        private readonly IMotorBody _body;
        private readonly MotorState _state;
        private readonly List<IMotorModule> _modules;

        private float _externalSpeedMultiplier = 1f;
        private Vector3 _pendingForce;

        /// <summary>Read-only access to the motor state.</summary>
        public IReadOnlyMotorState State => _state;

        /// <summary>Direct access to the body for external queries.</summary>
        public IMotorBody Body => _body;

        /// <summary>Fires every simulation tick with an immutable state snapshot.</summary>
        public event Action<MotorSnapshot>? StateUpdated;

        public MotorSimulation(IMotorBody body, IReadOnlyList<IMotorModule> modules)
        {
            _body = body;
            _state = new MotorState();
            _modules = new List<IMotorModule>(modules);
            _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            foreach (var m in _modules)
                m.Initialize(_state, _body, this);
        }

        /// <summary>
        /// Deterministic simulation step. Call from FixedUpdate or network tick.
        /// </summary>
        /// <summary>
        /// Set the external speed multiplier (from speed modifiers, debuffs, etc.).
        /// Applied multiplicatively to SpeedMultiplier each tick.
        /// </summary>
        public void SetExternalSpeedMultiplier(float multiplier)
        {
            _externalSpeedMultiplier = multiplier;
        }

        /// <summary>
        /// Queue an external force to be applied on the next tick.
        /// Forces accumulate until the next Simulate call.
        /// </summary>
        public void AddPendingForce(Vector3 force)
        {
            _pendingForce += force;
        }

        public void Simulate(MotorInput input, float deltaTime)
        {
            _state.ApplyInput(input);
            _state.SpeedMultiplier *= _externalSpeedMultiplier;
            _state.ExternalForce += _pendingForce;
            _pendingForce = Vector3.zero;
            _body.BeginFrame(_state, deltaTime);

            for (int i = 0; i < _modules.Count; i++)
            {
                if (_modules[i].IsActive)
                    _modules[i].Simulate(deltaTime);
            }

            _body.EndFrame(_state, deltaTime);
            StateUpdated?.Invoke(new MotorSnapshot(_state));
            _state.ResetPerFrame();
        }

        /// <summary>
        /// Visual update (camera rotation, smooth transitions). NOT part of simulation.
        /// </summary>
        public void VisualUpdate(float deltaTime)
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                if (_modules[i].IsActive)
                    _modules[i].VisualUpdate(deltaTime);
            }
        }

        /// <summary>Get a module by type.</summary>
        public T? GetModule<T>() where T : class, IMotorModule
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                if (_modules[i] is T typed) return typed;
            }
            return null;
        }

        /// <summary>Add a module at runtime. It will be initialized and sorted.</summary>
        public void AddModule(IMotorModule module)
        {
            module.Initialize(_state, _body, this);
            _modules.Add(module);
            _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>Remove a module at runtime.</summary>
        public bool RemoveModule(IMotorModule module)
        {
            return _modules.Remove(module);
        }

        /// <summary>
        /// Save the complete simulation state for prediction/reconciliation.
        /// </summary>
        public MotorStateSnapshot SaveState()
        {
            var snapshot = new MotorStateSnapshot
            {
                Body = _body.SaveState(),
                DesiredVelocity = _state.DesiredVelocity,
                CurrentVelocity = _state.CurrentVelocity,
                ExternalForce = _state.ExternalForce,
                GroundNormal = _state.GroundNormal,
                GroundAngle = _state.GroundAngle,
                SpeedMultiplier = _state.SpeedMultiplier,
                GravityMultiplier = _state.GravityMultiplier,
                IsGrounded = _state.IsGrounded,
                IsSprinting = _state.IsSprinting,
                IsCrouching = _state.IsCrouching,
                IsInAir = _state.IsInAir,
                SkipDefaultPhysics = _state.SkipDefaultPhysics,
            };

            var writer = new ModuleStateWriter(64);
            for (int i = 0; i < _modules.Count; i++)
            {
                if (_modules[i] is IStatefulModule stateful)
                    stateful.SaveState(ref writer);
            }
            snapshot.ModuleStates = writer.ToArray();

            return snapshot;
        }

        /// <summary>
        /// Restore simulation state from a snapshot. Used for reconciliation.
        /// </summary>
        public void RestoreState(MotorStateSnapshot snapshot)
        {
            _body.RestoreState(snapshot.Body);

            _state.DesiredVelocity = snapshot.DesiredVelocity;
            _state.CurrentVelocity = snapshot.CurrentVelocity;
            _state.ExternalForce = snapshot.ExternalForce;
            _state.GroundNormal = snapshot.GroundNormal;
            _state.GroundAngle = snapshot.GroundAngle;
            _state.SpeedMultiplier = snapshot.SpeedMultiplier;
            _state.GravityMultiplier = snapshot.GravityMultiplier;
            _state.IsGrounded = snapshot.IsGrounded;
            _state.IsSprinting = snapshot.IsSprinting;
            _state.IsCrouching = snapshot.IsCrouching;
            _state.IsInAir = snapshot.IsInAir;
            _state.SkipDefaultPhysics = snapshot.SkipDefaultPhysics;

            if (snapshot.ModuleStates is { Length: > 0 })
            {
                var reader = new ModuleStateReader(snapshot.ModuleStates);
                for (int i = 0; i < _modules.Count; i++)
                {
                    if (_modules[i] is IStatefulModule stateful)
                        stateful.RestoreState(ref reader);
                }
            }
        }
    }
}
