using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Rubickanov.UI
{
    /// <summary>
    /// Corner busy indicator on the overlay layer, styled by classes (<see cref="RootClass"/>, <see cref="IconClass"/>,
    /// <see cref="LabelClass"/>). Only the rotation is set in code.
    /// </summary>
    public sealed class SpinnerHost : ISpinnerHost, IDisposable
    {
        private const string RootElementName = "spinner-host-root";

        // USS classes, styled by Runtime/Styles/Default.uss.
        public const string RootClass = "spinner";
        public const string IconClass = "spinner__icon";
        public const string LabelClass = "spinner__label";

        private readonly VisualElement _overlayLayer;

        private VisualElement? _root;
        private VisualElement? _icon;
        private Label? _label;

        private readonly List<Handle> _activeHandles = new();
        private IVisualElementScheduledItem? _rotation;
        private float _angle;
        private bool _disposed;

        public SpinnerHost(VisualElement root)
        {
            _overlayLayer = UILayerElements.Require(root, UILayer.Overlay);
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

            // The panel's scheduler runs it on every panel update while the spinner is on a panel.
            if (_rotation == null)
                _rotation = _root.schedule.Execute(Rotate).Every(0);
            else
                _rotation.Resume();
        }

        private void Detach()
        {
            _rotation?.Pause();

            if (_root != null && _root.parent != null)
                _root.RemoveFromHierarchy();
        }

        private void Rotate(TimerState timer)
        {
            _angle = (_angle + timer.deltaTime * 0.36f) % 360f;
            if (_icon != null)
                _icon.style.rotate = new Rotate(new Angle(_angle, AngleUnit.Degree));
        }

        private void EnsureBuilt()
        {
            if (_root != null) return;

            _root = new VisualElement { name = RootElementName, pickingMode = PickingMode.Ignore };
            _root.AddToClassList(RootClass);

            _icon = new VisualElement { name = "spinner-host-icon", pickingMode = PickingMode.Ignore };
            _icon.AddToClassList(IconClass);
            _root.Add(_icon);

            _label = new Label { name = "spinner-host-label", pickingMode = PickingMode.Ignore };
            _label.AddToClassList(LabelClass);
            _label.style.display = DisplayStyle.None;
            _root.Add(_label);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _activeHandles.Clear();
            Detach();
            _rotation = null;
            _root = null;
            _icon = null;
            _label = null;
        }

        private sealed class Handle : IDisposable
        {
            private readonly SpinnerHost _owner;
            public string? Label { get; }
            private bool _disposed;

            public Handle(SpinnerHost owner, string? label)
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
