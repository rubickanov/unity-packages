namespace Rubickanov.Motor
{
    /// <summary>
    /// Contract for a motor module. Modules are pure C# classes that read/write
    /// shared <see cref="MotorState"/> and interact with <see cref="IMotorBody"/>.
    /// </summary>
    public interface IMotorModule
    {
        /// <summary>Execution order. Lower runs first.</summary>
        int Priority { get; }

        bool IsActive { get; set; }

        /// <summary>Called once when the module is added to a simulation.</summary>
        void Initialize(MotorState state, IMotorBody body);

        /// <summary>Deterministic simulation step. Called from FixedUpdate or network tick.</summary>
        void Simulate(float deltaTime);

        /// <summary>Visual-only update (camera, smooth transitions). Not part of simulation.</summary>
        void VisualUpdate(float deltaTime);
    }
}
