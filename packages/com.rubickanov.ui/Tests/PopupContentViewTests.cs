using System;
using System.Collections.Generic;
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

            _popups.Create().Title("Invite").Content<FriendsView>(viewModel).Open();

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

            _popups.Create().Content<FriendsView>(new FakeViewModel()).Open();
            _popups.Create().Content<FriendsView>(new FakeViewModel()).Open();

            Assert.AreEqual(2, PopupLayer.Query(className: PopupStyle.Content).ToList().Count);
        }

        [Test]
        public async Task Close_ContentView_ViewModelDisposedWithTheElements()
        {
            await _ui.Register<FriendsView>();
            var animation = new ControlledAnimation();
            var viewModel = new FakeViewModel();
            var popup = _popups.Create().Content<FriendsView>(viewModel).Animation(animation).Open();
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

            _popups.Create().Content<FriendsView>(new FakeViewModel()).Open();

            Assert.IsTrue(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public void Open_UnregisteredView_ThrowsAndDisposesViewModel()
        {
            var viewModel = new FakeViewModel();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _popups.Create().Content<FriendsView>(viewModel).Open());

            StringAssert.Contains(nameof(FriendsView), ex.Message);
            Assert.AreEqual(1, viewModel.DisposeCalls);
            Assert.IsNull(PopupLayer.Q(className: PopupStyle.Panel));
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public async Task Open_WrongViewModelType_ThrowsArgument()
        {
            await _ui.Register<FriendsView>();

            Assert.Throws<ArgumentException>(() =>
                _popups.Create().Content<FriendsView>(new OtherViewModel()).Open());
        }

        [Test]
        public async Task CreateDialog_WithContentView_ShowsView()
        {
            await _ui.Register<FriendsView>();

            _popups.CreateDialog("Invite").Content<FriendsView>(new FakeViewModel()).Button("Close", "close").Open();

            var content = _root.Q(className: PopupStyle.Content);
            Assert.IsNotNull(content);
            Assert.AreEqual("friends", content.name);
        }

        [Test]
        public async Task ShowView_PopupView_FillsPopupLayerWithoutChrome()
        {
            await _ui.Register<FriendsView>();

            var popup = _popups.ShowView<FriendsView>(new FakeViewModel());

            Assert.AreSame(PopupLayer, popup.Panel.parent);
            Assert.IsTrue(popup.Panel.ClassListContains(PopupStyle.View));
            Assert.IsTrue(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public async Task ShowView_ViewReachesItsPopup_ClosesItself()
        {
            await _ui.Register<SelfClosingView>();
            var popup = _popups.ShowView<SelfClosingView>(new FakeViewModel());

            SelfClosingView.Last!.CloseFromView();

            Assert.IsFalse(popup.IsOpen);
            Assert.AreEqual("done", (await popup.Result).ButtonId);
        }

        [Test]
        public async Task Back_ContentViewConsumes_PopupStaysOpen()
        {
            await _ui.Register<SelfClosingView>();
            var popup = _popups.ShowView<SelfClosingView>(new FakeViewModel());
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
            var popup = _popups.ShowView<UxmlFriendsView>(new FakeViewModel());

            _ui.Unregister<UxmlFriendsView>();
            var releasedWhileOpen = _loader.Released.Count;
            popup.Close();

            Assert.AreEqual(0, releasedWhileOpen);
            CollectionAssert.AreEqual(new[] { nameof(UxmlFriendsView) }, _loader.Released);
        }

        [Test]
        public async Task Unregister_WhilePopupShowsView_ViewStillCreatesChildren()
        {
            await _ui.Register<ListContent>();
            _popups.ShowView<ListContent>(new FakeViewModel());

            _ui.Unregister<ListContent>();

            Assert.DoesNotThrow(() => ListContent.Last!.AddRow());
            CollectionAssert.IsEmpty(_loader.Released);
        }

        [Test]
        public async Task Open_ContentViewWithAnimation_PlaysIt()
        {
            await _ui.Register<AnimatedView>();
            AnimatedView.TestAnimation = new ControlledAnimation();

            _popups.ShowView<AnimatedView>(new FakeViewModel());

            Assert.AreEqual(1, AnimatedView.TestAnimation.Shows.Count);
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

        public sealed class ChildRow : View<FakeViewModel>
        {
            protected override UILayer Layer => UILayer.HUD;
            protected override void OnBind() { }
        }

        public sealed class ListContent : View<FakeViewModel>
        {
            public static ListContent? Last;

            protected override UILayer Layer => UILayer.Popup;
            protected override IReadOnlyList<Type> ChildViews => new[] { typeof(ChildRow) };
            protected override void OnBind() => Last = this;
            public void AddRow() => CreateChild<ChildRow, FakeViewModel>(new FakeViewModel(), Root);
        }

        public sealed class SelfClosingView : View<FakeViewModel>
        {
            public static SelfClosingView? Last;
            public bool ConsumeBack;

            protected override UILayer Layer => UILayer.Popup;
            protected override string? UxmlName => null;
            protected override void OnBind() => Last = this;
            protected override bool OnBack() => ConsumeBack;
            public void CloseFromView() => Popup!.Close("done", PopupCloseReason.Button);
        }

        public sealed class AnimatedView : View<FakeViewModel>
        {
            public static ControlledAnimation TestAnimation = new();

            protected override UILayer Layer => UILayer.Popup;
            protected override string? UxmlName => null;
            protected override IViewAnimation Animation => TestAnimation;
            protected override void OnBind() { }
        }
    }
}
