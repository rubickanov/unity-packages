using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public sealed class UIToolkitAnimationTarget : IAnimationTarget
    {
        private readonly VisualElement _element;

        public UIToolkitAnimationTarget(VisualElement element)
        {
            _element = element;
        }

        public float Opacity
        {
            get => _element.resolvedStyle.opacity;
            set => _element.style.opacity = value;
        }

        public float TranslateX
        {
            get => _element.resolvedStyle.translate.x;
            set => _element.style.translate = new Translate(value, TranslateY);
        }

        public float TranslateY
        {
            get => _element.resolvedStyle.translate.y;
            set => _element.style.translate = new Translate(TranslateX, value);
        }

        public float ScaleX
        {
            get => _element.resolvedStyle.scale.value.x;
            set => _element.style.scale = new StyleScale(new Scale(new Vector3(value, ScaleY, 1f)));
        }

        public float ScaleY
        {
            get => _element.resolvedStyle.scale.value.y;
            set => _element.style.scale = new StyleScale(new Scale(new Vector3(ScaleX, value, 1f)));
        }

        public void SetVisible(bool visible)
        {
            _element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void ResetAnimationState()
        {
            _element.style.opacity = StyleKeyword.Null;
            _element.style.translate = StyleKeyword.Null;
            _element.style.scale = StyleKeyword.Null;
        }
    }
}
