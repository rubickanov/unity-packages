using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public class DynamicPopup : UIToolkitView<DynamicDialogViewModel>
    {
        internal override string? UxmlName => null;

        protected override UniTask OnBind()
        {
            var overlay = new VisualElement();
            overlay.AddToClassList("dialog-overlay");
            overlay.pickingMode = PickingMode.Position;

            var panel = new VisualElement();
            panel.AddToClassList("dialog-panel");

            // Title
            var title = new Label(ViewModel.Title);
            title.AddToClassList("dialog-title");
            panel.Add(title);

            // Image (optional)
            if (ViewModel.Image != null)
            {
                var img = new VisualElement();
                img.AddToClassList("dialog-image");
                img.style.backgroundImage = new StyleBackground(ViewModel.Image);
                img.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                panel.Add(img);
            }

            // Message (optional)
            if (!string.IsNullOrEmpty(ViewModel.Message))
            {
                var msg = new Label(ViewModel.Message);
                msg.AddToClassList("dialog-message");
                msg.style.whiteSpace = WhiteSpace.Normal;
                panel.Add(msg);
            }

            // Custom content (optional)
            if (ViewModel.ContentFactory != null)
            {
                var content = ViewModel.ContentFactory();
                content.AddToClassList("dialog-content");
                panel.Add(content);
            }

            // Input (optional)
            if (ViewModel.HasInput)
            {
                var field = new TextField();
                field.AddToClassList("dialog-input");
                field.value = ViewModel.InputDefault;
                field.textEdition.placeholder = ViewModel.InputPlaceholder;
                BindValueChanged<TextField, string>(field, v => ViewModel.InputValue = v);
                panel.Add(field);
            }

            // Buttons
            var btnRow = new VisualElement();
            btnRow.AddToClassList("dialog-buttons");
            btnRow.style.flexDirection = FlexDirection.Row;

            foreach (var btn in ViewModel.Buttons)
            {
                var button = new Button { text = btn.Text };
                button.AddToClassList("dialog-btn");

                if (btn.IsPrimary)
                    button.AddToClassList("dialog-btn--primary");

                var id = btn.Id;
                BindButton(button, () => ViewModel.Complete(id));
                btnRow.Add(button);
            }

            panel.Add(btnRow);
            overlay.Add(panel);
            Root.Add(overlay);

            Root.RegisterCallback<NavigationCancelEvent>(OnNavigationCancel);
            TrackUnbind(() => Root.UnregisterCallback<NavigationCancelEvent>(OnNavigationCancel));
            Root.focusable = true;
            Root.Focus();

            return UniTask.CompletedTask;
        }

        protected override void OnUnbind()
        {
            Root.Clear();
        }

        private void OnNavigationCancel(NavigationCancelEvent evt)
        {
            evt.StopPropagation();
            ViewModel.CompleteWithLast();
        }
    }
}
