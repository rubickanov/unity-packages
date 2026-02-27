using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public interface IUIService
    {
        UniTask Register<T>(UILayer layer) where T : class, IView;
        void Unregister<T>() where T : IView;
        T Get<T>() where T : IView;
        UniTask ShowScreen<T>(ViewModelBase viewModel) where T : IView;
        void HideScreen<T>() where T : IView;
        void HideAllScreens();
        UniTask ShowPopup<T>(ViewModelBase viewModel) where T : IView;
        void HidePopup<T>() where T : IView;
        void HideTopPopup();
        UniTask HideScreenAsync<T>(float duration = 0.3f) where T : IView;
        UniTask HidePopupAsync<T>(float duration = 0.3f) where T : IView;
        UniTask HideTopPopupAsync(float duration = 0.3f);
    }
}
