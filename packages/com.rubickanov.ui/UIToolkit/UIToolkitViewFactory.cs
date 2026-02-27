using System;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public class UIToolkitViewFactory : IViewFactory
    {
        public delegate UniTask<VisualTreeAsset> UxmlLoader(string address);

        private readonly UxmlLoader _loadUxml;
        private readonly VisualElement _screenLayer;
        private readonly VisualElement _hudLayer;
        private readonly VisualElement _popupLayer;
        private readonly VisualElement _overlayLayer;

        public UIToolkitViewFactory(UIDocument document, UxmlLoader loadUxml)
        {
            _loadUxml = loadUxml;
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
                    var asset = await _loadUxml($"{uitkView.UxmlName}");
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
                uitkView.Initialize();
                GetLayerContainer(layer).Add(uitkView.Root);
            }

            return view;
        }

        public void Detach(IView view)
        {
            if (view is UIToolkitViewBase uitkView)
                uitkView.Root.RemoveFromHierarchy();
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
