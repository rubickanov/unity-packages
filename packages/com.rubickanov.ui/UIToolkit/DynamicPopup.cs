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
            overlay.AddToClassList(DialogStyle.Overlay);
            overlay.pickingMode = PickingMode.Position;

            var panel = new VisualElement();
            panel.AddToClassList(DialogStyle.Panel);

            var title = new Label(ViewModel.Title);
            title.AddToClassList(DialogStyle.Title);
            panel.Add(title);

            if (ViewModel.Image != null)
            {
                var img = new VisualElement();
                img.AddToClassList(DialogStyle.Image);
                img.style.backgroundImage = new StyleBackground(ViewModel.Image);
                img.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                panel.Add(img);
            }

            if (!string.IsNullOrEmpty(ViewModel.Message))
            {
                var msg = new Label(ViewModel.Message);
                msg.AddToClassList(DialogStyle.Message);
                msg.style.whiteSpace = WhiteSpace.Normal;
                panel.Add(msg);
            }

            if (ViewModel.ContentFactory != null)
            {
                var content = ViewModel.ContentFactory();
                content.AddToClassList(DialogStyle.Content);
                panel.Add(content);
            }

            if (ViewModel.HasInput)
            {
                var field = new TextField();
                field.AddToClassList(DialogStyle.Input);
                field.value = ViewModel.InputDefault;
                field.textEdition.placeholder = ViewModel.InputPlaceholder;
                BindValueChanged<TextField, string>(field, v => ViewModel.InputValue = v);
                panel.Add(field);
            }

            var btnRow = new VisualElement();
            btnRow.AddToClassList(DialogStyle.Buttons);
            btnRow.style.flexDirection = FlexDirection.Row;

            foreach (var btn in ViewModel.Buttons)
            {
                var button = new Button { text = btn.Text };
                button.AddToClassList(DialogStyle.Button);

                if (btn.IsPrimary)
                    button.AddToClassList(DialogStyle.ButtonPrimary);

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
