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

            _popups.Create().Title("Invite").Content<FriendsView>(viewModel).Open();

            var content = PopupLayer.Q(className: PopupStyle.Content);
            Assert.IsNotNull(content);
            Assert.AreEqual("friends", content.name);
            Assert.AreSame(viewModel, FriendsView.LastBound);
            Assert.AreEqual(ViewState.Hidden, _ui.Get<FriendsView>().State);
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
        public async Task Dialog_WithContentView_ShowsView()
        {
            await _ui.Register<FriendsView>();
            var dialogs = new DialogService(_popups);

            dialogs.CreateDialog("Invite").WithContent<FriendsView>(new FakeViewModel())
                .AddButton("Close", "close").ShowAsync().Forget();

            var content = _root.Q(className: PopupStyle.Content);
            Assert.IsNotNull(content);
            Assert.AreEqual("friends", content.name);
        }

        public sealed class FriendsView : View<FakeViewModel>
        {
            public static FakeViewModel? LastBound;

            protected override UILayer Layer => UILayer.Popup;
            protected override void OnInitialize() => Root.name = "friends";
            protected override void OnBind() => LastBound = ViewModel;
        }
    }
}
