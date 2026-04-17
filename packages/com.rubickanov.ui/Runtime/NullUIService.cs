using System;
using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public class NullUIService : IUIService
    {
        public UniTask Register<T>(UILayer layer) where T : class, IView => UniTask.CompletedTask;
        public void Unregister<T>() where T : IView { }

        public T Get<T>() where T : IView
            => throw new NotSupportedException("UI service is not available (NullUIService).");

        public UniTask Show<T>(ViewModelBase viewModel) where T : IView => UniTask.CompletedTask;
        public void Hide<T>() where T : IView { }
        public UniTask HideAsync<T>(float duration = 0.3f) where T : IView => UniTask.CompletedTask;
        public void HideTop() { }
        public UniTask HideTopAsync(float duration = 0.3f) => UniTask.CompletedTask;
        public void HideAll() { }
        public UniTask HideAllAsync(float duration = 0.3f) => UniTask.CompletedTask;
    }
}
