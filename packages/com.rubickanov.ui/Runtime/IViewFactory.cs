using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public interface IViewFactory
    {
        UniTask<IView> Create<T>(UILayer layer) where T : class, IView;
        void Detach(IView view);
    }
}
