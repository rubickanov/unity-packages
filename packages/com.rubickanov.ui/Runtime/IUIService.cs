using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public interface IUIService
    {
        UniTask Register<T>(UILayer layer) where T : class, IView;
        void Unregister<T>() where T : IView;
        T Get<T>() where T : IView;

        UniTask Show<T>(ViewModelBase viewModel) where T : IView;
        void Hide<T>() where T : IView;
        UniTask HideAsync<T>(float duration = 0.3f) where T : IView;

        void HideTop();
        UniTask HideTopAsync(float duration = 0.3f);
        void HideAll();
        UniTask HideAllAsync(float duration = 0.3f);
    }
}
