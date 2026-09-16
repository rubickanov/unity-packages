using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Tests
{
    public static class TestRoot
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
    }

    /// <summary>Loader that returns an empty asset built in code and records released handles.</summary>
    public sealed class RecordingUxmlLoader
    {
        public readonly List<string> Loaded = new();
        public readonly List<string> Released = new();

        public UniTask<(VisualTreeAsset asset, IDisposable handle)> Load(string name)
        {
            Loaded.Add(name);
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            return UniTask.FromResult<(VisualTreeAsset, IDisposable)>((asset, new Handle(this, name)));
        }

        private sealed class Handle : IDisposable
        {
            private readonly RecordingUxmlLoader _owner;
            private readonly string _name;

            public Handle(RecordingUxmlLoader owner, string name)
            {
                _owner = owner;
                _name = name;
            }

            public void Dispose() => _owner.Released.Add(_name);
        }
    }

    public class FakeViewModel : ViewModelBase { }

    public abstract class FakeView : View<FakeViewModel>
    {
        protected override string? UxmlName => null;

        public int BindCalls { get; private set; }
        public int ShowCalls { get; private set; }
        public int HideCalls { get; private set; }
        public ViewModelBase? LastViewModel { get; private set; }

        public Exception? ThrowOnBind { get; set; }
        public Exception? ThrowOnShowAsync { get; set; }

        protected override UniTask OnBind()
        {
            BindCalls++;
            LastViewModel = ViewModel;
            if (ThrowOnBind != null) throw ThrowOnBind;
            return UniTask.CompletedTask;
        }

        protected override UniTask OnShowAsync()
        {
            ShowCalls++;
            if (ThrowOnShowAsync != null) throw ThrowOnShowAsync;
            return UniTask.CompletedTask;
        }

        protected override void OnViewHide() => HideCalls++;
    }

    public sealed class FakeViewA : FakeView { }
    public sealed class FakeViewB : FakeView { }
    public sealed class FakeViewC : FakeView { }

    public sealed class FakeUxmlView : View<FakeViewModel>
    {
        protected override UniTask OnBind() => UniTask.CompletedTask;
    }
}
