using UnityEngine;

namespace Rubickanov.UI.UGUI
{
    public sealed class UGUIAnimationTarget : IAnimationTarget
    {
        private readonly CanvasGroup _canvasGroup;
        private readonly RectTransform _rectTransform;

        private readonly float _initialAlpha;
        private readonly Vector2 _initialPosition;
        private readonly Vector3 _initialScale;

        public UGUIAnimationTarget(CanvasGroup canvasGroup, RectTransform rectTransform)
        {
            _canvasGroup = canvasGroup;
            _rectTransform = rectTransform;

            _initialAlpha = canvasGroup.alpha;
            _initialPosition = rectTransform.anchoredPosition;
            _initialScale = rectTransform.localScale;
        }

        public float Opacity
        {
            get => _canvasGroup.alpha;
            set => _canvasGroup.alpha = value;
        }

        public float TranslateX
        {
            get => _rectTransform.anchoredPosition.x - _initialPosition.x;
            set
            {
                var pos = _rectTransform.anchoredPosition;
                pos.x = _initialPosition.x + value;
                _rectTransform.anchoredPosition = pos;
            }
        }

        // Negated: UIToolkit Y-down vs UGUI Y-up
        public float TranslateY
        {
            get => -(_rectTransform.anchoredPosition.y - _initialPosition.y);
            set
            {
                var pos = _rectTransform.anchoredPosition;
                pos.y = _initialPosition.y - value;
                _rectTransform.anchoredPosition = pos;
            }
        }

        public float ScaleX
        {
            get => _rectTransform.localScale.x;
            set => _rectTransform.localScale = new Vector3(value, _rectTransform.localScale.y, 1f);
        }

        public float ScaleY
        {
            get => _rectTransform.localScale.y;
            set => _rectTransform.localScale = new Vector3(_rectTransform.localScale.x, value, 1f);
        }

        public void SetVisible(bool visible)
        {
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.blocksRaycasts = visible;
            _canvasGroup.interactable = visible;
        }

        public void ResetAnimationState()
        {
            _canvasGroup.alpha = _initialAlpha;
            _rectTransform.anchoredPosition = _initialPosition;
            _rectTransform.localScale = _initialScale;
        }
    }
}
