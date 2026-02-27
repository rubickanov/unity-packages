using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public interface IView
    {
        bool IsVisible { get; }
        UniTask Bind(ViewModelBase viewModel);
        void Show();
        void Hide();
        void Destroy();

        UniTask ShowAsync(float duration = 0.3f) { Show(); return UniTask.CompletedTask; }
        UniTask HideAsync(float duration = 0.3f) { Hide(); return UniTask.CompletedTask; }
    }
}
