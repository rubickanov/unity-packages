using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Rubickanov.UI.UGUI
{
    public class UGUIViewFactory : IViewFactory
    {
        public delegate UniTask<GameObject> PrefabLoader(string address);

        private readonly PrefabLoader _loadPrefab;
        private readonly IViewServiceResolver? _serviceResolver;
        private readonly RectTransform _screenLayer;
        private readonly RectTransform _hudLayer;
        private readonly RectTransform _popupLayer;
        private readonly RectTransform _overlayLayer;

        public UGUIViewFactory(Transform root, PrefabLoader loadPrefab, IViewServiceResolver? serviceResolver = null)
        {
            _loadPrefab = loadPrefab;
            _serviceResolver = serviceResolver;
            _screenLayer = FindLayer(root, "ScreenLayer");
            _hudLayer = FindLayer(root, "HUDLayer");
            _popupLayer = FindLayer(root, "PopupLayer");
            _overlayLayer = FindLayer(root, "OverlayLayer");
        }

        public async UniTask<IView> Create<T>(UILayer layer) where T : class, IView
        {
            var layerTransform = GetLayerContainer(layer);

            // Determine prefab name from type
            var prefabName = typeof(T).Name;

            var prefab = await _loadPrefab(prefabName);
            var instance = UnityEngine.Object.Instantiate(prefab, layerTransform);
            instance.name = prefabName;

            var rectTransform = instance.GetComponent<RectTransform>();
            if (rectTransform != null)
                StretchFill(rectTransform);

            var view = instance.GetComponent<UGUIViewBase>();
            if (view == null)
                throw new InvalidOperationException(
                    $"Prefab '{prefabName}' does not have a {nameof(UGUIViewBase)} component.");

            view.ViewFactory = this;
            view.ServiceResolver = _serviceResolver;
            view.Initialize();
            instance.SetActive(false);

            return view;
        }

        public void Detach(IView view)
        {
            if (view is UGUIViewBase uguiView)
                uguiView.transform.SetParent(null);
        }

        private RectTransform GetLayerContainer(UILayer layer) => layer switch
        {
            UILayer.Screen => _screenLayer,
            UILayer.HUD => _hudLayer,
            UILayer.Popup => _popupLayer,
            UILayer.Overlay => _overlayLayer,
            _ => _screenLayer
        };

        private static RectTransform FindLayer(Transform root, string name)
        {
            var child = root.Find(name);
            if (child == null)
                throw new InvalidOperationException(
                    $"UI root is missing required child '{name}'. " +
                    "Expected hierarchy: UIRoot/ScreenLayer, HUDLayer, PopupLayer, OverlayLayer.");
            return (RectTransform)child;
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
