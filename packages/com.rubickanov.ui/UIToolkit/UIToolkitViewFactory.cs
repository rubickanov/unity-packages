using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public class UIToolkitViewFactory : IViewFactory
    {
        public delegate UniTask<(VisualTreeAsset asset, IDisposable handle)> UxmlLoader(string address);

        private readonly UxmlLoader _loadUxml;
        private readonly IViewServiceResolver? _serviceResolver;
        private readonly Dictionary<IView, IDisposable> _uxmlHandles = new();
        private readonly VisualElement _screenLayer;
        private readonly VisualElement _hudLayer;
        private readonly VisualElement _popupLayer;
        private readonly VisualElement _overlayLayer;

        public UIToolkitViewFactory(UIDocument document, UxmlLoader loadUxml, IViewServiceResolver? serviceResolver = null)
        {
            _loadUxml = loadUxml;
            _serviceResolver = serviceResolver;
            var root = document.rootVisualElement;
            _screenLayer = RequireLayer(root, "screen-layer");
            _hudLayer = RequireLayer(root, "hud-layer");
            _popupLayer = RequireLayer(root, "popup-layer");
            _overlayLayer = RequireLayer(root, "overlay-layer");
        }

        private static VisualElement RequireLayer(VisualElement root, string name)
        {
            var element = root.Q(name);
            if (element == null)
                throw new InvalidOperationException(
                    $"UIDocument root is missing required child '{name}'. " +
                    "Expected elements: screen-layer, hud-layer, popup-layer, overlay-layer.");
            return element;
        }

        public async UniTask<IView> Create<T>(UILayer layer) where T : class, IView
        {
            var view = Activator.CreateInstance<T>();

            if (view is UIToolkitViewBase uitkView)
            {
                if (uitkView.UxmlName != null)
                {
                    var (asset, handle) = await _loadUxml(uitkView.UxmlName);
                    if (asset == null)
                        throw new InvalidOperationException(
                            $"Failed to load UXML '{uitkView.UxmlName}' for view {typeof(T).Name}.");
                    _uxmlHandles[view] = handle;
                    uitkView.Root = asset.CloneTree();
                }
                else
                {
                    uitkView.Root = new VisualElement();
                }

                uitkView.Root.style.position = Position.Absolute;
                uitkView.Root.style.left = uitkView.Root.style.top =
                    uitkView.Root.style.right = uitkView.Root.style.bottom = 0;
                uitkView.Root.pickingMode = PickingMode.Ignore;
                uitkView.Root.style.display = DisplayStyle.None;

                uitkView.ViewFactory = this;
                uitkView.ServiceResolver = _serviceResolver;
                uitkView.Initialize();
                GetLayerContainer(layer).Add(uitkView.Root);
            }

            return view;
        }

        public void Detach(IView view)
        {
            if (_uxmlHandles.Remove(view, out var handle))
                handle.Dispose();
        }

        private VisualElement GetLayerContainer(UILayer layer) => layer switch
        {
            UILayer.Screen => _screenLayer,
            UILayer.HUD => _hudLayer,
            UILayer.Popup => _popupLayer,
            UILayer.Overlay => _overlayLayer,
            _ => _screenLayer
        };
    }
}
