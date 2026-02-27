using Cysharp.Threading.Tasks;

namespace Rubickanov.UI.UIToolkit
{
    public class AlertViewModel : ViewModelBase
    {
        public string Title { get; }
        public string Message { get; }
        public string ButtonText { get; }
        public UniTaskCompletionSource CompletionSource { get; } = new();
        public UniTask Result => CompletionSource.Task;

        public AlertViewModel(string title, string message, string buttonText)
        {
            Title = title;
            Message = message;
            ButtonText = buttonText;
        }
    }
}
