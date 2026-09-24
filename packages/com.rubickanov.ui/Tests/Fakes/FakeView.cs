using System;
using System.Collections.Generic;
using System.Threading;
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

    /// <summary>
    /// Loader returning empty assets built in code and recording loads and released handles. With
    /// <see cref="Deferred"/> a load completes only when the test calls <see cref="Complete"/>.
    /// </summary>
    public sealed class RecordingUxmlLoader
    {
        public readonly List<string> Loaded = new();
        public readonly List<string> Released = new();
        public bool Deferred { get; set; }

        private readonly Dictionary<string, UniTaskCompletionSource<(VisualTreeAsset? asset, IDisposable? handle)>> _pending = new();

        public UniTask<(VisualTreeAsset? asset, IDisposable? handle)> Load(string name)
        {
            Loaded.Add(name);
            if (!Deferred)
                return UniTask.FromResult(CreateResult(name));

            var source = new UniTaskCompletionSource<(VisualTreeAsset? asset, IDisposable? handle)>();
            _pending[name] = source;
            return source.Task;
        }

        public void Complete(string name)
        {
            var source = _pending[name];
            _pending.Remove(name);
            source.TrySetResult(CreateResult(name));
        }

        private (VisualTreeAsset? asset, IDisposable? handle) CreateResult(string name)
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            asset.name = name;
            return (asset, new Handle(this, name));
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

    /// <summary>Animation whose every show and hide finishes only when the test completes it.</summary>
    public sealed class ControlledAnimation : IViewAnimation
    {
        public readonly List<UniTaskCompletionSource> Shows = new();
        public readonly List<UniTaskCompletionSource> Hides = new();

        public UniTask PlayShowAsync(VisualElement target, CancellationToken ct) => Start(Shows, ct);
        public UniTask PlayHideAsync(VisualElement target, CancellationToken ct) => Start(Hides, ct);
        public void Reset(VisualElement target) { }

        public void CompleteShow() => Shows[^1].TrySetResult();
        public void CompleteHide() => Hides[^1].TrySetResult();
        public void FailShow(Exception exception) => Shows[^1].TrySetException(exception);

        private static UniTask Start(List<UniTaskCompletionSource> list, CancellationToken ct)
        {
            var source = new UniTaskCompletionSource();
            list.Add(source);
            return source.Task.AttachExternalCancellation(ct);
        }
    }

    public class FakeViewModel : ViewModelBase
    {
        public int DisposeCalls { get; private set; }
        protected override void OnDispose() => DisposeCalls++;
    }

    public sealed class OtherViewModel : ViewModelBase { }

    public abstract class FakeView : View<FakeViewModel>
    {
        protected override string? UxmlName => null;
        protected override IViewAnimation Animation => TestAnimation ?? NoneAnimation.Instance;

        public IViewAnimation? TestAnimation { get; set; }
        public int BindCalls { get; private set; }
        public int UnbindCalls { get; private set; }
        public FakeViewModel? LastViewModel { get; private set; }
        public Exception? ThrowOnBind { get; set; }
        public Exception? ThrowOnUnbind { get; set; }

        protected override void OnBind()
        {
            BindCalls++;
            LastViewModel = ViewModel;
            if (ThrowOnBind != null) throw ThrowOnBind;
        }

        protected override void OnUnbind()
        {
            UnbindCalls++;
            if (ThrowOnUnbind != null) throw ThrowOnUnbind;
        }
    }

    public abstract class FakeScreen : FakeView
    {
        protected override UILayer Layer => UILayer.Screen;
    }

    public abstract class FakePopup : FakeView
    {
        protected override UILayer Layer => UILayer.Popup;
    }

    public sealed class ScreenA : FakeScreen { }
    public sealed class ScreenB : FakeScreen { }
    public sealed class PopupA : FakePopup { }
    public sealed class PopupB : FakePopup { }

    public sealed class HudA : FakeView
    {
        protected override UILayer Layer => UILayer.HUD;
    }

    public sealed class UxmlScreen : View<FakeViewModel>
    {
        protected override UILayer Layer => UILayer.Screen;
        protected override void OnBind() { }
    }

    public sealed class UxmlPopup : View<FakeViewModel>
    {
        protected override UILayer Layer => UILayer.Popup;
        protected override void OnBind() { }
    }
}
