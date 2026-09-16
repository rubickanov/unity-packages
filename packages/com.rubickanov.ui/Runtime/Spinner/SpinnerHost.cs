using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.UIToolkit
{
    public sealed class UIToolkitSpinnerHost : ISpinnerHost, IDisposable
    {
        private const string OverlayLayerName = "overlay-layer";
        private const string RootElementName = "spinner-host-root";

        private readonly VisualElement _overlayLayer;

        private VisualElement? _root;
        private VisualElement? _icon;
        private Label? _label;

        private readonly List<Handle> _activeHandles = new();
        private CancellationTokenSource? _rotationCts;
        private float _angle;
        private bool _disposed;

        public UIToolkitSpinnerHost(UIDocument document)
        {
            var root = document.rootVisualElement;
            var overlay = root.Q(OverlayLayerName);
            if (overlay == null)
                throw new InvalidOperationException(
                    $"UIDocument root is missing required child '{OverlayLayerName}'. UIToolkitSpinnerHost requires an overlay-layer to attach to.");
            _overlayLayer = overlay;
        }

        public IDisposable Show(string? label = null)
        {
            if (_disposed)
                return NoOpDisposable.Instance;

            EnsureBuilt();

            var handle = new Handle(this, label);
            _activeHandles.Add(handle);
            Refresh();
            return handle;
        }

        private void Release(Handle handle)
        {
            if (_disposed) return;
            if (!_activeHandles.Remove(handle)) return;
            Refresh();
        }

        private void Refresh()
        {
            if (_root == null) return;

            if (_activeHandles.Count == 0)
            {
                Detach();
                return;
            }

            var top = _activeHandles[^1];
            if (_label != null)
            {
                if (string.IsNullOrEmpty(top.Label))
                {
                    _label.text = string.Empty;
                    _label.style.display = DisplayStyle.None;
                }
                else
                {
                    _label.text = top.Label;
                    _label.style.display = DisplayStyle.Flex;
                }
            }

            if (_root.parent == null)
                _overlayLayer.Add(_root);

            if (_rotationCts == null)
            {
                _rotationCts = new CancellationTokenSource();
                RunRotationLoop(_rotationCts.Token).Forget();
            }
        }

        private void Detach()
        {
            _rotationCts?.Cancel();
            _rotationCts?.Dispose();
            _rotationCts = null;

            if (_root != null && _root.parent != null)
                _root.RemoveFromHierarchy();
        }

        private async UniTaskVoid RunRotationLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                _angle = (_angle + Time.unscaledDeltaTime * 360f) % 360f;
                if (_icon != null)
                    _icon.style.rotate = new Rotate(new Angle(_angle, AngleUnit.Degree));

                try
                {
                    await UniTask.NextFrame(ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private void EnsureBuilt()
        {
            if (_root != null) return;

            _root = new VisualElement { name = RootElementName };
            _root.pickingMode = PickingMode.Ignore;
            var s = _root.style;
            s.position = Position.Absolute;
            s.right = 24f;
            s.bottom = 24f;
            s.flexDirection = FlexDirection.Row;
            s.alignItems = Align.Center;
            s.paddingLeft = s.paddingRight = s.paddingTop = s.paddingBottom = 8f;
            s.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            s.borderTopLeftRadius = s.borderTopRightRadius =
                s.borderBottomLeftRadius = s.borderBottomRightRadius = 12f;

            _icon = new VisualElement { name = "spinner-host-icon" };
            _icon.pickingMode = PickingMode.Ignore;
            var ics = _icon.style;
            ics.width = ics.height = 28f;
            ics.borderTopLeftRadius = ics.borderTopRightRadius =
                ics.borderBottomLeftRadius = ics.borderBottomRightRadius = 14f;
            ics.borderTopWidth = ics.borderRightWidth =
                ics.borderBottomWidth = ics.borderLeftWidth = 3f;
            ics.borderTopColor = Color.white;
            var transparent = new Color(1f, 1f, 1f, 0f);
            ics.borderRightColor = transparent;
            ics.borderBottomColor = transparent;
            ics.borderLeftColor = transparent;
            ics.transformOrigin = new TransformOrigin(
                new Length(50f, LengthUnit.Percent),
                new Length(50f, LengthUnit.Percent));
            _root.Add(_icon);

            _label = new Label { name = "spinner-host-label" };
            _label.pickingMode = PickingMode.Ignore;
            var ls = _label.style;
            ls.marginLeft = 10f;
            ls.color = Color.white;
            ls.fontSize = 12f;
            ls.display = DisplayStyle.None;
            _root.Add(_label);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _activeHandles.Clear();
            Detach();
            _root = null;
            _icon = null;
            _label = null;
        }

        private sealed class Handle : IDisposable
        {
            private readonly UIToolkitSpinnerHost _owner;
            public string? Label { get; }
            private bool _disposed;

            public Handle(UIToolkitSpinnerHost owner, string? label)
            {
                _owner = owner;
                Label = label;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner.Release(this);
            }
        }

        private sealed class NoOpDisposable : IDisposable
        {
            public static readonly NoOpDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
