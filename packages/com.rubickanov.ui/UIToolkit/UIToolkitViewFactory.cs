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
            _screenLayer = root.Q("screen-layer");
            _hudLayer = root.Q("hud-layer");
            _popupLayer = root.Q("popup-layer");
            _overlayLayer = root.Q("overlay-layer");
        }

        public async UniTask<IView> Create<T>(UILayer layer) where T : class, IView
        {
            var view = Activator.CreateInstance<T>();

            if (view is UIToolkitViewBase uitkView)
            {
                if (uitkView.UxmlName != null)
                {
                    var (asset, handle) = await _loadUxml(uitkView.UxmlName);
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
