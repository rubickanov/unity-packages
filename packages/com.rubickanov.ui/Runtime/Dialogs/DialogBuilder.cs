using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public class DialogBuilder
    {
        private readonly Func<DynamicDialogViewModel, UniTask<DialogResult>> _showFunc;
        private readonly DynamicDialogViewModel _vm;

        internal DialogBuilder(string title, Func<DynamicDialogViewModel, UniTask<DialogResult>> showFunc)
        {
            _showFunc = showFunc;
            _vm = new DynamicDialogViewModel(title);
        }

        public DialogBuilder WithMessage(string message)
        {
            _vm.Message = message;
            return this;
        }

        public DialogBuilder WithImage(Texture2D texture)
        {
            _vm.Image = texture;
            return this;
        }

        public DialogBuilder WithContent(Func<VisualElement> contentFactory)
        {
            _vm.ContentFactory = contentFactory;
            return this;
        }

        public DialogBuilder WithInput(string placeholder = "", string defaultValue = "")
        {
            _vm.HasInput = true;
            _vm.InputPlaceholder = placeholder;
            _vm.InputDefault = defaultValue;
            _vm.InputValue = defaultValue;
            return this;
        }

        public DialogBuilder AddButton(string text, string id, bool isPrimary = false)
        {
            _vm.Buttons.Add(new ButtonConfig(text, id, isPrimary));
            return this;
        }

        public UniTask<DialogResult> ShowAsync() => _showFunc(_vm);
    }
}
