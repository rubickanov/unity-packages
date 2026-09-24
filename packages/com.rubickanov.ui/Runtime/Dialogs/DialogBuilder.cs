using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// Builds a centered modal dialog shown through <see cref="IPopupService"/>.
    /// </summary>
    public class DialogBuilder
    {
        private readonly IPopupService _popups;
        private readonly PopupConfig _config;

        internal DialogBuilder(string title, IPopupService popups)
        {
            _popups = popups;
            _config = new PopupConfig
            {
                Title = title,
                Behaviour = PopupBehaviour.Modal,
                Placement = PopupPlacement.ScreenCenter(),
                RootClass = PopupStyle.Dialog,
                CloseTriggers = PopupCloseTriggers.Escape | PopupCloseTriggers.ActionButton
            };
        }

        public DialogBuilder WithMessage(string message)
        {
            _config.Message = message;
            return this;
        }

        public DialogBuilder WithImage(Texture2D texture)
        {
            _config.Icon = texture;
            return this;
        }

        public DialogBuilder WithContent(Func<VisualElement> contentFactory)
        {
            _config.ContentFactory = contentFactory;
            return this;
        }

        /// <summary>
        /// Shows the registered view <typeparamref name="TView"/>, bound to <paramref name="viewModel"/>, as content.
        /// The dialog owns the view model. See <see cref="PopupConfig.ContentViewType"/>.
        /// </summary>
        public DialogBuilder WithContent<TView>(ViewModelBase viewModel) where TView : View
        {
            _config.ContentViewType = typeof(TView);
            _config.ContentViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            return this;
        }

        public DialogBuilder WithInput(string placeholder = "", string defaultValue = "")
        {
            _config.HasInput = true;
            _config.InputPlaceholder = placeholder;
            _config.InputDefault = defaultValue;
            return this;
        }

        public DialogBuilder AddButton(string text, string id, bool isPrimary = false)
        {
            _config.Buttons.Add(new PopupButton(text, id, isPrimary));
            return this;
        }

        public async UniTask<DialogResult> ShowAsync()
        {
            var result = await _popups.Open(_config).Result;

            // Escape or a close without a button maps to the last button.
            var buttons = _config.Buttons;
            var buttonId = result.ButtonId
                ?? (buttons.Count > 0 ? buttons[^1].Id : string.Empty);
            return new DialogResult(buttonId, _config.HasInput ? result.InputText : null);
        }
    }
}
