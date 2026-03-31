namespace Rubickanov.UI
{
    public interface IViewServiceResolver
    {
        T? Resolve<T>() where T : class;
    }
}
