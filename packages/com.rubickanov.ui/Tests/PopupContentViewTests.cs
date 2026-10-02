using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class PopupContentViewTests
    {
        private VisualElement _root = null!;
        private RecordingUxmlLoader _loader = null!;
        private UIService _ui = null!;
        private PopupHost _popups = null!;

        [SetUp]
        public void SetUp()
        {
            _root = TestRoot.Create();
            _loader = new RecordingUxmlLoader();
            _ui = new UIService(_root, _loader.Load);
            _popups = new PopupHost(_root, _ui);
        }

        [TearDown]
        public void TearDown()
        {
            _popups?.Dispose();
            _ui?.Dispose();
        }

        private VisualElement PopupLayer => _root.Q("popup-layer");

        [Test]
        public async Task Open_RegisteredView_NewInstanceBoundInsidePanel()
        {
            await _ui.Register<FriendsView>();
            var viewModel = new FakeViewModel();

            _popups.Create().Content<FriendsView, FakeViewModel>(viewModel).Open();

            var content = PopupLayer.Q(className: PopupStyle.Content);
            Assert.IsNotNull(content);
            Assert.AreEqual("friends", content.name);
            Assert.AreSame(viewModel, FriendsView.LastBound);
            CollectionAssert.AreEqual(new[] { nameof(FriendsView) }, _loader.Loaded);
        }

        [Test]
        public async Task Open_TwoPopupsWithSameView_EachHasItsOwnInstance()
        {
            await _ui.Register<FriendsView>();

            _popups.Create().Content<FriendsView, FakeViewModel>(new FakeViewModel()).Open();
            _popups.Create().Content<FriendsView, FakeViewModel>(new FakeViewModel()).Open();

            Assert.AreEqual(2, PopupLayer.Query(className: PopupStyle.Content).ToList().Count);
        }

        [Test]
        public async Task Close_ContentView_ViewModelDisposedWithTheElements()
        {
            await _ui.Register<FriendsView>();
            var animation = new ControlledAnimation();
            var viewModel = new FakeViewModel();
            var popup = _popups.Create().Content<FriendsView, FakeViewModel>(viewModel).Animation(animation).Open();
            animation.CompleteShow();

            popup.Close();
            var disposedDuringHide = viewModel.DisposeCalls;
            animation.CompleteHide();

            Assert.AreEqual(0, disposedDuringHide);
            Assert.AreEqual(1, viewModel.DisposeCalls);
            Assert.IsNull(PopupLayer.Q(className: PopupStyle.Panel));
        }

        [Test]
        public async Task Open_ContentView_CapturesPointer()
        {
            await _ui.Register<FriendsView>();

            _popups.Create().Content<FriendsView, FakeViewModel>(new FakeViewModel()).Open();

            Assert.IsTrue(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public void Open_UnregisteredView_ThrowsAndDisposesViewModel()
        {
            var viewModel = new FakeViewModel();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _popups.Create().Content<FriendsView, FakeViewModel>(viewModel).Open());

            StringAssert.Contains(nameof(FriendsView), ex.Message);
            Assert.AreEqual(1, viewModel.DisposeCalls);
            Assert.IsNull(PopupLayer.Q(className: PopupStyle.Panel));
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public async Task ShowView_PopupView_FillsPopupLayerWithoutChrome()
        {
            await _ui.Register<FriendsView>();

            var popup = _popups.ShowView<FriendsView, FakeViewModel>(new FakeViewModel());

            Assert.AreSame(PopupLayer, popup.Panel.parent.parent);
            Assert.IsTrue(popup.Panel.ClassListContains(PopupStyle.View));
            Assert.IsTrue(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public async Task ShowView_ViewReachesItsPopup_ClosesItself()
        {
            await _ui.Register<SelfClosingView>();
            var popup = _popups.ShowView<SelfClosingView, FakeViewModel>(new FakeViewModel());

            SelfClosingView.Last!.CloseFromView();

            Assert.IsFalse(popup.IsOpen);
            Assert.AreEqual("done", (await popup.Result).Id);
        }

        [Test]
        public async Task Back_ContentViewConsumes_PopupStaysOpen()
        {
            await _ui.Register<SelfClosingView>();
            var popup = _popups.ShowView<SelfClosingView, FakeViewModel>(new FakeViewModel());
            SelfClosingView.Last!.ConsumeBack = true;

            var first = _ui.Back();
            SelfClosingView.Last.ConsumeBack = false;
            var openAfterFirst = popup.IsOpen;
            _ui.Back();

            Assert.IsTrue(first);
            Assert.IsTrue(openAfterFirst);
            Assert.IsFalse(popup.IsOpen);
            Assert.AreEqual(PopupCloseReason.Escape, (await popup.Result).Reason);
        }

        [Test]
        public async Task Unregister_WhilePopupShowsView_UxmlReleasedWhenPopupCloses()
        {
            await _ui.Register<UxmlFriendsView>();
            var popup = _popups.ShowView<UxmlFriendsView, FakeViewModel>(new FakeViewModel());

            _ui.Unregister<UxmlFriendsView>();
            var releasedWhileOpen = _loader.Released.Count;
            popup.Close();

            Assert.AreEqual(0, releasedWhileOpen);
            CollectionAssert.AreEqual(new[] { nameof(UxmlFriendsView) }, _loader.Released);
        }

        [Test]
        public async Task Open_ContentViewWithAnimation_PlaysIt()
        {
            await _ui.Register<AnimatedView>();
            AnimatedView.TestAnimation = new ControlledAnimation();

            _popups.ShowView<AnimatedView, FakeViewModel>(new FakeViewModel());

            Assert.AreEqual(1, ((ControlledAnimation)AnimatedView.TestAnimation).Shows.Count);
        }

        [Test]
        public async Task Open_ContentViewBuilderTwice_ThrowsInsteadOfBindingDisposedViewModel()
        {
            await _ui.Register<FriendsView>();
            var builder = _popups.Create().Content<FriendsView, FakeViewModel>(new FakeViewModel());
            builder.Open().Close();

            var ex = Assert.Throws<InvalidOperationException>(() => builder.Open());

            StringAssert.Contains(nameof(FriendsView), ex.Message);
        }

        [Test]
        public void Open_FactoryBuilderTwice_BuildsContentForEach()
        {
            var built = 0;
            var builder = _popups.Create().Content(() =>
            {
                built++;
                return new VisualElement();
            });

            builder.Open();
            builder.Open();

            Assert.AreEqual(2, built);
        }

        [Test]
        public async Task Open_ContentViewWithAnimationTarget_PlaysOnIt()
        {
            await _ui.Register<StripPopup>();
            var animation = new RecordingAnimation();
            StripPopup.TestAnimation = animation;

            var popup = _popups.ShowView<StripPopup, FakeViewModel>(new FakeViewModel());
            popup.Close();

            Assert.AreEqual(2, animation.Targets.Count);
            Assert.AreEqual("strip", animation.Targets[0].name);
            Assert.AreSame(animation.Targets[0], animation.Targets[1]);
        }

        [Test]
        public async Task Open_ContentViewAnimatingItsRoot_PlaysOnThePanel()
        {
            await _ui.Register<AnimatedView>();
            var animation = new RecordingAnimation();
            AnimatedView.TestAnimation = animation;

            var popup = _popups.ShowView<AnimatedView, FakeViewModel>(new FakeViewModel());

            CollectionAssert.AreEqual(new[] { popup.Panel }, animation.Targets);
        }

        public sealed class FriendsView : View<FakeViewModel>
        {
            public static FakeViewModel? LastBound;

            protected override UILayer Layer => UILayer.Popup;
            protected override void OnInitialize() => Root.name = "friends";
            protected override void OnBind() => LastBound = ViewModel;
        }

        public sealed class UxmlFriendsView : View<FakeViewModel>
        {
            protected override UILayer Layer => UILayer.Popup;
            protected override void OnBind() { }
        }

        public sealed class SelfClosingView : View<FakeViewModel>
        {
            public static SelfClosingView? Last;
            public bool ConsumeBack;

            protected override UILayer Layer => UILayer.Popup;
            protected override string? UxmlName => null;
            protected override void OnBind() => Last = this;
            protected override bool OnBack() => ConsumeBack;
            public void CloseFromView() => Popup!.Close("done");
        }

        public sealed class AnimatedView : View<FakeViewModel>
        {
            public static IViewAnimation TestAnimation = new ControlledAnimation();

            protected override UILayer Layer => UILayer.Popup;
            protected override string? UxmlName => null;
            protected override IViewAnimation Animation => TestAnimation;
            protected override void OnBind() { }
        }

        public sealed class StripPopup : View<FakeViewModel>
        {
            public static IViewAnimation TestAnimation = NoneAnimation.Instance;
            private readonly VisualElement _strip = new() { name = "strip" };

            protected override UILayer Layer => UILayer.Popup;
            protected override string? UxmlName => null;
            protected override IViewAnimation Animation => TestAnimation;
            protected override VisualElement AnimationTarget => _strip;
            protected override void OnInitialize() => Root.Add(_strip);
            protected override void OnBind() { }
        }
    }
}
