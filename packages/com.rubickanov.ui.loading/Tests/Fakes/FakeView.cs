using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Rubickanov.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Loading.Tests
{
    internal static class TestRoot
    {
        public static VisualElement Create()
        {
            var root = new VisualElement();
            root.Add(new VisualElement { name = "screen-layer" });
            root.Add(new VisualElement { name = "hud-layer" });
            root.Add(new VisualElement { name = "popup-layer" });
            root.Add(new VisualElement { name = "overlay-layer" });
            return root;
        }

        public static UniTask<(VisualTreeAsset asset, IDisposable handle)> NoUxml(string name)
            => throw new InvalidOperationException($"Code-only test views load no UXML, asked for '{name}'.");
    }

    /// <summary>Loader whose loads complete only when the test calls <see cref="Complete"/>.</summary>
    internal sealed class DeferredUxmlLoader
    {
        private readonly Dictionary<string, UniTaskCompletionSource<(VisualTreeAsset asset, IDisposable handle)>> _pending = new();

        public UniTask<(VisualTreeAsset asset, IDisposable handle)> Load(string name)
        {
            var source = new UniTaskCompletionSource<(VisualTreeAsset asset, IDisposable handle)>();
            _pending[name] = source;
            return source.Task;
        }

        public void Complete(string name)
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            _pending[name].TrySetResult((asset, new NoRelease()));
        }

        private sealed class NoRelease : IDisposable
        {
            public void Dispose() { }
        }
    }

    internal sealed class FakeViewModel : ViewModelBase { }

    internal abstract class FakeView : View<FakeViewModel>
    {
        protected override string? UxmlName => null;
        protected override void OnBind() { }
    }

    internal sealed class FakeViewA : FakeView
    {
        protected override UILayer Layer => UILayer.Screen;
    }

    internal sealed class FakeViewB : FakeView
    {
        protected override UILayer Layer => UILayer.Popup;
    }

    internal sealed class FakeViewC : FakeView
    {
        protected override UILayer Layer => UILayer.HUD;
    }

    internal sealed class UxmlViewA : View<FakeViewModel>
    {
        protected override UILayer Layer => UILayer.Screen;
        protected override void OnBind() { }
    }
}
