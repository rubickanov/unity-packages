using System;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    public enum UILayer { Screen, HUD, Popup, Overlay }

    /// <summary>The four layer elements of a UI root, found by name.</summary>
    internal readonly struct UILayerElements
    {
        private readonly VisualElement _screen;
        private readonly VisualElement _hud;
        private readonly VisualElement _popup;
        private readonly VisualElement _overlay;

        public UILayerElements(VisualElement root)
        {
            _screen = Require(root, UILayer.Screen);
            _hud = Require(root, UILayer.HUD);
            _popup = Require(root, UILayer.Popup);
            _overlay = Require(root, UILayer.Overlay);
        }

        public VisualElement this[UILayer layer] => layer switch
        {
            UILayer.Screen => _screen,
            UILayer.HUD => _hud,
            UILayer.Popup => _popup,
            UILayer.Overlay => _overlay,
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null)
        };

        /// <exception cref="InvalidOperationException"><paramref name="root"/> has no element for the layer.</exception>
        public static VisualElement Require(VisualElement root, UILayer layer)
        {
            var name = NameOf(layer);
            return root.Q(name) ?? throw new InvalidOperationException(
                $"UI root is missing required child '{name}'. " +
                "Expected elements: screen-layer, hud-layer, popup-layer, overlay-layer.");
        }

        private static string NameOf(UILayer layer) => layer switch
        {
            UILayer.Screen => "screen-layer",
            UILayer.HUD => "hud-layer",
            UILayer.Popup => "popup-layer",
            UILayer.Overlay => "overlay-layer",
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null)
        };
    }
}
