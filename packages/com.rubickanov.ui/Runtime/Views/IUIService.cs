using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public interface IUIService
    {
        UniTask Register<T>(UILayer layer) where T : View;
        void Unregister<T>() where T : View;
        T Get<T>() where T : View;

        UniTask Show<T>(ViewModelBase viewModel) where T : View;
        void Hide<T>() where T : View;
        UniTask HideAsync<T>() where T : View;

        void HideTop();
        UniTask HideTopAsync();
        void HideAll();
        UniTask HideAllAsync();
    }
}
