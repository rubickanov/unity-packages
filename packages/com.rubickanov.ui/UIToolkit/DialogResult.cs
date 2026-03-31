namespace Rubickanov.UI.UIToolkit
{
    public class DialogResult
    {
        public string ButtonId { get; }
        public string? InputText { get; }

        public DialogResult(string buttonId, string? inputText)
        {
            ButtonId = buttonId;
            InputText = inputText;
        }
    }
}
