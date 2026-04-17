using System;

namespace Rubickanov.UI
{
    public interface IViewServiceResolver
    {
        T? Resolve<T>() where T : class;

        T Require<T>() where T : class
            => Resolve<T>() ?? throw new InvalidOperationException(
                $"Service {typeof(T).Name} is not registered in IViewServiceResolver.");
    }
}
