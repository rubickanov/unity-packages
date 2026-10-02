using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class ScreenHistoryTests
    {
        private UIService _ui = null!;
        private PopupHost _popups = null!;

        [SetUp]
        public void SetUp()
        {
            // Code-only views register synchronously.
            var root = TestRoot.Create();
            _ui = new UIService(root, new RecordingUxmlLoader().Load);
            _popups = new PopupHost(root, _ui);
            _ui.Register<ScreenA>().GetAwaiter().GetResult();
            _ui.Register<ScreenB>().GetAwaiter().GetResult();
            _ui.Register<ScreenC>().GetAwaiter().GetResult();
            _ui.Register<PopupA>().GetAwaiter().GetResult();
            _ui.Register<ReadingScreen>().GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            _popups?.Dispose();
            _ui?.Dispose();
        }

        [Test]
        public async Task NavigateBack_AfterTwoScreens_ShowsFirstWithNewViewModel()
        {
            var created = 0;
            await _ui.Navigate<ScreenA, FakeViewModel>(() => { created++; return new FakeViewModel(); });
            var firstViewModel = _ui.Get<ScreenA>().LastViewModel;
            await _ui.Navigate<ScreenB, FakeViewModel>(() => new FakeViewModel());

            var navigated = await _ui.NavigateBack();

            Assert.IsTrue(navigated);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenA>().State);
            Assert.AreEqual(ViewState.Hidden, _ui.Get<ScreenB>().State);
            Assert.AreEqual(2, created);
            Assert.AreNotSame(firstViewModel, _ui.Get<ScreenA>().LastViewModel);
            Assert.IsFalse(_ui.CanNavigateBack);
        }

        [Test]
        public async Task NavigateBack_OnlyOneScreen_ReturnsFalseAndKeepsIt()
        {
            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());

            var navigated = await _ui.NavigateBack();

            Assert.IsFalse(navigated);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenA>().State);
        }

        [Test]
        public async Task Back_ThreeScreens_ReturnsOneScreenPerPressThenNotConsumed()
        {
            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB, FakeViewModel>(() => new FakeViewModel());
            await _ui.Navigate<ScreenC, FakeViewModel>(() => new FakeViewModel());

            var first = _ui.Back();
            var shownAfterFirst = _ui.Get<ScreenB>().State;
            var second = _ui.Back();
            var shownAfterSecond = _ui.Get<ScreenA>().State;
            var third = _ui.Back();

            Assert.IsTrue(first);
            Assert.AreEqual(ViewState.Shown, shownAfterFirst);
            Assert.IsTrue(second);
            Assert.AreEqual(ViewState.Shown, shownAfterSecond);
            Assert.IsFalse(third);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenA>().State);
        }

        [Test]
        public async Task Back_PopupOverNavigatedScreen_ClosesPopupBeforeGoingBack()
        {
            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB, FakeViewModel>(() => new FakeViewModel());
            var popup = _popups.ShowView<PopupA, FakeViewModel>(new FakeViewModel());

            _ui.Back();

            Assert.IsFalse(popup.IsOpen);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenB>().State);
            Assert.IsTrue(_ui.CanNavigateBack);
        }

        [Test]
        public async Task Show_Screen_ClearsHistory()
        {
            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB, FakeViewModel>(() => new FakeViewModel());

            await _ui.Show<ScreenC, FakeViewModel>(new FakeViewModel());

            Assert.IsFalse(_ui.CanNavigateBack);
            Assert.IsFalse(_ui.Back());
            CollectionAssert.IsEmpty(_ui.DebugScreenHistory);
        }

        [Test]
        public async Task ShowView_Popup_KeepsHistory()
        {
            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB, FakeViewModel>(() => new FakeViewModel());

            _popups.ShowView<PopupA, FakeViewModel>(new FakeViewModel());

            Assert.IsTrue(_ui.CanNavigateBack);
        }

        [Test]
        public async Task Navigate_ScreenAlreadyInHistory_DropsScreensAboveIt()
        {
            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB, FakeViewModel>(() => new FakeViewModel());
            await _ui.Navigate<ScreenC, FakeViewModel>(() => new FakeViewModel());

            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());

            CollectionAssert.AreEqual(new[] { typeof(ScreenA) }, _ui.DebugScreenHistory);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenA>().State);
            Assert.AreEqual(ViewState.Hidden, _ui.Get<ScreenC>().State);
        }

        [Test]
        public void Navigate_PopupView_ThrowsInvalidOperation()
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _ui.Navigate<PopupA, FakeViewModel>(() => new FakeViewModel()));
        }

        [Test]
        public async Task Hide_CurrentScreen_ClearsHistory()
        {
            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB, FakeViewModel>(() => new FakeViewModel());

            _ui.Hide<ScreenB>();

            Assert.IsFalse(_ui.CanNavigateBack);
        }

        [Test]
        public async Task Unregister_ScreenInMiddleOfHistory_ReturnSkipsIt()
        {
            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB, FakeViewModel>(() => new FakeViewModel());
            await _ui.Navigate<ScreenC, FakeViewModel>(() => new FakeViewModel());

            _ui.Unregister<ScreenB>();
            await _ui.NavigateBack();

            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenA>().State);
            Assert.IsFalse(_ui.CanNavigateBack);
        }

        public sealed class ScreenC : FakeScreen { }

        [Test]
        public async Task Back_ScreenShownOverOpenPopupView_PopupAnswersFirst()
        {
            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());
            var popup = _popups.ShowView<PopupA, FakeViewModel>(new FakeViewModel());
            await _ui.Navigate<ScreenB, FakeViewModel>(() => new FakeViewModel());

            var consumed = _ui.Back();

            Assert.IsTrue(consumed);
            Assert.IsFalse(popup.IsOpen);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenB>().State);
            Assert.IsTrue(_ui.CanNavigateBack);
        }

        [Test]
        public async Task Back_HandlerPushedBeforeScreen_RunsBeforeScreen()
        {
            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());
            var handled = false;
            using var handle = _ui.PushBackHandler(() => handled = true);
            await _ui.Navigate<ScreenB, FakeViewModel>(() => new FakeViewModel());

            var consumed = _ui.Back();

            Assert.IsTrue(consumed);
            Assert.IsTrue(handled);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenB>().State);
        }

        [Test]
        public async Task Back_ModalPopupWithoutEscapeOverScreen_ScreenStays()
        {
            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB, FakeViewModel>(() => new FakeViewModel());
            var modal = _popups.Create().Content(() => new VisualElement()).Modal().Open();

            var consumed = _ui.Back();

            Assert.IsTrue(consumed);
            Assert.IsTrue(modal.IsOpen);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenB>().State);
            Assert.AreEqual(ViewState.Hidden, _ui.Get<ScreenA>().State);
        }

        [Test]
        public async Task Back_ModalPopupClosed_ScreenGoesBackAgain()
        {
            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB, FakeViewModel>(() => new FakeViewModel());
            _popups.Create().Content(() => new VisualElement()).Modal().Open().Close();

            _ui.Back();

            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenA>().State);
        }

        [Test]
        public async Task Back_ScreenOverrideCallsBase_ViewModelStillBoundAfterBase()
        {
            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());
            await _ui.Navigate<ReadingScreen, FakeViewModel>(() => new FakeViewModel());

            _ui.Back();

            Assert.IsNotNull(_ui.Get<ReadingScreen>().ViewModelAfterBase);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenA>().State);
            Assert.AreEqual(ViewState.Hidden, _ui.Get<ReadingScreen>().State);
        }

        /// <summary>Reads its view model after the default back, as a screen that cues a sound on going back does.</summary>
        public sealed class ReadingScreen : FakeScreen
        {
            public FakeViewModel? ViewModelAfterBase;

            protected override bool OnBack()
            {
                var back = base.OnBack();
                ViewModelAfterBase = ViewModel;
                return back;
            }
        }
    }
}
