using System;
using Cysharp.Threading.Tasks;
using Rubickanov.UI;
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

    internal sealed class FakeViewModel : ViewModelBase { }

    internal abstract class FakeView : View<FakeViewModel>
    {
        protected override string? UxmlName => null;
        protected override UniTask OnBind() => UniTask.CompletedTask;
    }

    internal sealed class FakeViewA : FakeView { }
    internal sealed class FakeViewB : FakeView { }
    internal sealed class FakeViewC : FakeView { }
}
