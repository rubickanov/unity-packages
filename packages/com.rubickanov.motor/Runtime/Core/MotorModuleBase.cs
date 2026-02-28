using System;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Convenience base class for motor modules.
    /// Provides access to shared state and body.
    /// </summary>
    [Serializable]
    public abstract class MotorModuleBase : IMotorModule
    {
        public virtual int Priority => 0;
        public bool IsActive { get; set; } = true;

        protected MotorState State { get; private set; } = default!;
        protected IMotorBody Body { get; private set; } = default!;

        public void Initialize(MotorState state, IMotorBody body)
        {
            State = state;
            Body = body;
            OnInitialize();
        }

        protected virtual void OnInitialize() { }
        public virtual void Simulate(float deltaTime) { }
        public virtual void VisualUpdate(float deltaTime) { }
    }
}
