using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public class TooltipService : IDisposable
    {
        private readonly VisualElement _container;
        private readonly VisualElement _overlayLayer;
        private readonly StyleSheet? _defaultStyleSheet;

        private VisualElement? _currentContent;

        public float Offset { get; set; } = 8f;

        public TooltipService(UIDocument document, StyleSheet? defaultStyleSheet = null)
        {
            _defaultStyleSheet = defaultStyleSheet;
            _overlayLayer = document.rootVisualElement.Q("overlay-layer")
                ?? throw new InvalidOperationException(
                    "TooltipService requires an 'overlay-layer' element in the UIDocument root.");

            _container = new VisualElement();
            _container.name = "tooltip-container";
            _container.AddToClassList("tooltip-container");
            _container.pickingMode = PickingMode.Ignore;
            _container.style.position = Position.Absolute;
            _container.style.display = DisplayStyle.None;

            if (_defaultStyleSheet != null)
                _container.styleSheets.Add(_defaultStyleSheet);

            _overlayLayer.Add(_container);
        }

        public void Show(VisualElement anchor, string text)
        {
            var label = new Label(text);
            label.AddToClassList("tooltip-text");
            label.pickingMode = PickingMode.Ignore;
            Show(anchor, label);
        }

        public void Show(VisualElement anchor, VisualElement content)
        {
            var bounds = anchor.worldBound;
            var position = new Vector2(
                bounds.x + bounds.width * 0.5f,
                bounds.yMax + Offset
            );

            ShowAtPosition(position, content);
        }

        public void Show(Vector2 screenPosition, string text)
        {
            var label = new Label(text);
            label.AddToClassList("tooltip-text");
            label.pickingMode = PickingMode.Ignore;
            Show(screenPosition, label);
        }

        public void Show(Vector2 screenPosition, VisualElement content)
        {
            var panelPosition = RuntimePanelUtils.ScreenToPanel(
                _overlayLayer.panel, screenPosition);

            ShowAtPosition(panelPosition, content);
        }

        public void Hide()
        {
            _container.style.display = DisplayStyle.None;

            if (_currentContent != null)
            {
                _container.Remove(_currentContent);
                _currentContent = null;
            }
        }

        public void UpdatePosition(VisualElement anchor)
        {
            if (_container.style.display == DisplayStyle.None) return;

            var bounds = anchor.worldBound;
            var position = new Vector2(
                bounds.x + bounds.width * 0.5f,
                bounds.yMax + Offset
            );

            ApplyPosition(position);
        }

        public void UpdatePosition(Vector2 screenPosition)
        {
            if (_container.style.display == DisplayStyle.None) return;

            var panelPosition = RuntimePanelUtils.ScreenToPanel(
                _overlayLayer.panel, screenPosition);

            ApplyPosition(panelPosition);
        }

        public void Dispose()
        {
            _container.RemoveFromHierarchy();
        }

        private void ShowAtPosition(Vector2 panelPosition, VisualElement content)
        {
            if (_currentContent != null)
                _container.Remove(_currentContent);

            _currentContent = content;
            _container.Add(content);

            _container.style.display = DisplayStyle.Flex;

            _container.RegisterCallbackOnce<GeometryChangedEvent>(_ => ApplyPosition(panelPosition));
            ApplyPosition(panelPosition);
        }

        private void ApplyPosition(Vector2 position)
        {
            var containerWidth = _container.resolvedStyle.width;
            var containerHeight = _container.resolvedStyle.height;
            var panelWidth = _overlayLayer.resolvedStyle.width;
            var panelHeight = _overlayLayer.resolvedStyle.height;

            var x = position.x - containerWidth * 0.5f;
            var y = position.y;

            if (float.IsNaN(containerWidth) || float.IsNaN(panelWidth))
            {
                _container.style.left = position.x;
                _container.style.top = position.y;
                return;
            }

            // Clamp to screen bounds
            if (x + containerWidth > panelWidth)
                x = panelWidth - containerWidth;
            if (x < 0)
                x = 0;

            // If overflows bottom, show above the anchor
            if (y + containerHeight > panelHeight)
                y = position.y - containerHeight - Offset * 2f;
            if (y < 0)
                y = 0;

            _container.style.left = x;
            _container.style.top = y;
        }
    }
}
