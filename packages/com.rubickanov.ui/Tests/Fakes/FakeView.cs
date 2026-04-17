using System;
using Cysharp.Threading.Tasks;

namespace Rubickanov.UI.Tests
{
    public class FakeView : IView
    {
        public bool IsVisible { get; private set; }
        public int BindCalls { get; private set; }
        public int HideCalls { get; private set; }
        public int ShowCalls { get; private set; }
        public int DestroyCalls { get; private set; }
        public ViewModelBase? LastViewModel { get; private set; }

        public Exception? ThrowOnBind { get; set; }
        public Exception? ThrowOnShowAsync { get; set; }

        public UniTask Bind(ViewModelBase viewModel)
        {
            BindCalls++;
            LastViewModel = viewModel;
            if (ThrowOnBind != null) throw ThrowOnBind;
            return UniTask.CompletedTask;
        }

        public void Show()
        {
            ShowCalls++;
            IsVisible = true;
        }

        public void Hide()
        {
            HideCalls++;
            IsVisible = false;
        }

        public void Destroy() => DestroyCalls++;

        public UniTask ShowAsync(float duration = 0.3f)
        {
            if (ThrowOnShowAsync != null) throw ThrowOnShowAsync;
            Show();
            return UniTask.CompletedTask;
        }

        public UniTask HideAsync(float duration = 0.3f)
        {
            Hide();
            return UniTask.CompletedTask;
        }
    }
}
