using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public class DynamicDialogViewModel : ViewModelBase
    {
        public string Title { get; }
        public string? Message { get; set; }
        public Texture2D? Image { get; set; }
        public Func<VisualElement>? ContentFactory { get; set; }
        public bool HasInput { get; set; }
        public string InputPlaceholder { get; set; } = "";
        public string InputDefault { get; set; } = "";
        public string InputValue { get; set; } = "";
        public List<ButtonConfig> Buttons { get; } = new();

        private readonly UniTaskCompletionSource<DialogResult> _completion = new();
        public UniTask<DialogResult> Result => _completion.Task;

        public DynamicDialogViewModel(string title) => Title = title;

        public void Complete(string buttonId)
            => _completion.TrySetResult(new DialogResult(buttonId, HasInput ? InputValue : null));

        public void CompleteWithLast()
            => Complete(Buttons[^1].Id);
    }

    public readonly struct ButtonConfig
    {
        public string Text { get; }
        public string Id { get; }
        public bool IsPrimary { get; }

        public ButtonConfig(string text, string id, bool isPrimary)
        {
            Text = text;
            Id = id;
            IsPrimary = isPrimary;
        }
    }
}
