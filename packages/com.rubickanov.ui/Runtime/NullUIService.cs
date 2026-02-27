using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public class NullUIService : IUIService
    {
        public UniTask Register<T>(UILayer layer) where T : class, IView => UniTask.CompletedTask;
        public void Unregister<T>() where T : IView { }
        public T Get<T>() where T : IView => default!;
        public UniTask ShowScreen<T>(ViewModelBase viewModel) where T : IView => UniTask.CompletedTask;
        public void HideScreen<T>() where T : IView { }
        public void HideAllScreens() { }
        public UniTask ShowPopup<T>(ViewModelBase viewModel) where T : IView => UniTask.CompletedTask;
        public void HidePopup<T>() where T : IView { }
        public void HideTopPopup() { }
        public UniTask HideScreenAsync<T>(float duration = 0.3f) where T : IView => UniTask.CompletedTask;
        public UniTask HidePopupAsync<T>(float duration = 0.3f) where T : IView => UniTask.CompletedTask;
        public UniTask HideTopPopupAsync(float duration = 0.3f) => UniTask.CompletedTask;
    }
}
