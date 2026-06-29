namespace Rubickanov.Motor
{
    /// <summary>
    /// Allows modules to query other modules by type.
    /// Implemented by <see cref="MotorSimulation"/>.
    /// </summary>
    public interface IModuleResolver
    {
        T? GetModule<T>() where T : class, IMotorModule;
    }
}
