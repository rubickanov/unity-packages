using Cysharp.Threading.Tasks;

namespace Rubickanov.UI.UIToolkit
{
    public class ConfirmViewModel : ViewModelBase
    {
        public string Title { get; }
        public string Message { get; }
        public string ConfirmText { get; }
        public string CancelText { get; }
        public UniTaskCompletionSource<bool> CompletionSource { get; } = new();
        public UniTask<bool> Result => CompletionSource.Task;

        public ConfirmViewModel(string title, string message, string confirmText, string cancelText)
        {
            Title = title;
            Message = message;
            ConfirmText = confirmText;
            CancelText = cancelText;
        }
    }
}
