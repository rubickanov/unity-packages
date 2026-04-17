using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public sealed class UIToolkitAnimationTarget : IAnimationTarget
    {
        private readonly VisualElement _element;
        private Vector2 _translate;
        private Vector2 _scale = Vector2.one;

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
            get => _translate.x;
            set { _translate.x = value; _element.style.translate = new Translate(_translate.x, _translate.y); }
        }

        public float TranslateY
        {
            get => _translate.y;
            set { _translate.y = value; _element.style.translate = new Translate(_translate.x, _translate.y); }
        }

        public float ScaleX
        {
            get => _scale.x;
            set { _scale.x = value; _element.style.scale = new StyleScale(new Scale(new Vector3(_scale.x, _scale.y, 1f))); }
        }

        public float ScaleY
        {
            get => _scale.y;
            set { _scale.y = value; _element.style.scale = new StyleScale(new Scale(new Vector3(_scale.x, _scale.y, 1f))); }
        }

        public void SetVisible(bool visible)
        {
            _element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void ResetAnimationState()
        {
            _translate = Vector2.zero;
            _scale = Vector2.one;
            _element.style.opacity = StyleKeyword.Null;
            _element.style.translate = StyleKeyword.Null;
            _element.style.scale = StyleKeyword.Null;
        }
    }
}
