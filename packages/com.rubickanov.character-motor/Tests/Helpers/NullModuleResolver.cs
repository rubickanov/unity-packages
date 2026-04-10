namespace Rubickanov.Motor.Tests
{
    /// <summary>
    /// Resolver stub for isolated module tests that don't query siblings.
    /// </summary>
    internal sealed class NullModuleResolver : IModuleResolver
    {
        public T? GetModule<T>() where T : class, IMotorModule => null;
    }
}
