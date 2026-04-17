using System;
using Cysharp.Threading.Tasks;
using Rubickanov.UI;

namespace Rubickanov.UI.Loading.Tests
{
    internal class FakeView : IView
    {
        public bool IsVisible { get; private set; }

        public UniTask Bind(ViewModelBase viewModel) => UniTask.CompletedTask;

        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Destroy() { }

        public UniTask ShowAsync(float duration = 0.3f)
        {
            Show();
            return UniTask.CompletedTask;
        }

        public UniTask HideAsync(float duration = 0.3f)
        {
            Hide();
            return UniTask.CompletedTask;
        }
    }

    internal sealed class FakeViewA : FakeView { }
    internal sealed class FakeViewB : FakeView { }
    internal sealed class FakeViewC : FakeView { }
}
