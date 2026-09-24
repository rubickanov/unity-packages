using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class ScreenHistoryTests
    {
        private UIService _ui = null!;

        [SetUp]
        public void SetUp()
        {
            // Code-only views register synchronously.
            _ui = new UIService(TestRoot.Create(), new RecordingUxmlLoader().Load);
            _ui.Register<ScreenA>().GetAwaiter().GetResult();
            _ui.Register<ScreenB>().GetAwaiter().GetResult();
            _ui.Register<ScreenC>().GetAwaiter().GetResult();
            _ui.Register<PopupA>().GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown() => _ui?.Dispose();

        [Test]
        public async Task NavigateBack_AfterTwoScreens_ShowsFirstWithNewViewModel()
        {
            var created = 0;
            await _ui.Navigate<ScreenA>(() => { created++; return new FakeViewModel(); });
            var firstViewModel = _ui.Get<ScreenA>().LastViewModel;
            await _ui.Navigate<ScreenB>(() => new FakeViewModel());

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
            await _ui.Navigate<ScreenA>(() => new FakeViewModel());

            var navigated = await _ui.NavigateBack();

            Assert.IsFalse(navigated);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenA>().State);
        }

        [Test]
        public async Task Back_ThreeScreens_ReturnsOneScreenPerPressThenNotConsumed()
        {
            await _ui.Navigate<ScreenA>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB>(() => new FakeViewModel());
            await _ui.Navigate<ScreenC>(() => new FakeViewModel());

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
        public async Task Back_PopupOverNavigatedScreen_HidesPopupBeforeGoingBack()
        {
            await _ui.Navigate<ScreenA>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB>(() => new FakeViewModel());
            await _ui.Show<PopupA>(new FakeViewModel());

            _ui.Back();

            Assert.AreEqual(ViewState.Hidden, _ui.Get<PopupA>().State);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenB>().State);
            Assert.IsTrue(_ui.CanNavigateBack);
        }

        [Test]
        public async Task Show_Screen_ClearsHistory()
        {
            await _ui.Navigate<ScreenA>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB>(() => new FakeViewModel());

            await _ui.Show<ScreenC>(new FakeViewModel());

            Assert.IsFalse(_ui.CanNavigateBack);
            Assert.IsFalse(_ui.Back());
            CollectionAssert.IsEmpty(_ui.DebugScreenHistory);
        }

        [Test]
        public async Task Show_Popup_KeepsHistory()
        {
            await _ui.Navigate<ScreenA>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB>(() => new FakeViewModel());

            await _ui.Show<PopupA>(new FakeViewModel());

            Assert.IsTrue(_ui.CanNavigateBack);
        }

        [Test]
        public async Task Navigate_ScreenAlreadyInHistory_DropsScreensAboveIt()
        {
            await _ui.Navigate<ScreenA>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB>(() => new FakeViewModel());
            await _ui.Navigate<ScreenC>(() => new FakeViewModel());

            await _ui.Navigate<ScreenA>(() => new FakeViewModel());

            CollectionAssert.AreEqual(new[] { typeof(ScreenA) }, _ui.DebugScreenHistory);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenA>().State);
            Assert.AreEqual(ViewState.Hidden, _ui.Get<ScreenC>().State);
        }

        [Test]
        public void Navigate_PopupView_ThrowsInvalidOperation()
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _ui.Navigate<PopupA>(() => new FakeViewModel()));
        }

        [Test]
        public async Task Navigate_FactoryReturnsWrongViewModel_ThrowsAndDisposesIt()
        {
            await _ui.Navigate<ScreenA>(() => new FakeViewModel());
            var wrong = new DisposableOtherViewModel();

            Assert.ThrowsAsync<ArgumentException>(async () => await _ui.Navigate<ScreenB>(() => wrong));

            Assert.IsTrue(wrong.Disposed);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenA>().State);
            CollectionAssert.AreEqual(new[] { typeof(ScreenA) }, _ui.DebugScreenHistory);
        }

        [Test]
        public async Task HideAll_ClearsHistory()
        {
            await _ui.Navigate<ScreenA>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB>(() => new FakeViewModel());

            _ui.HideAll();

            Assert.IsFalse(_ui.CanNavigateBack);
        }

        [Test]
        public async Task Hide_CurrentScreen_ClearsHistory()
        {
            await _ui.Navigate<ScreenA>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB>(() => new FakeViewModel());

            _ui.Hide<ScreenB>();

            Assert.IsFalse(_ui.CanNavigateBack);
        }

        [Test]
        public async Task Unregister_ScreenInMiddleOfHistory_ReturnSkipsIt()
        {
            await _ui.Navigate<ScreenA>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB>(() => new FakeViewModel());
            await _ui.Navigate<ScreenC>(() => new FakeViewModel());

            _ui.Unregister<ScreenB>();
            await _ui.NavigateBack();

            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenA>().State);
            Assert.IsFalse(_ui.CanNavigateBack);
        }

        public sealed class ScreenC : FakeScreen { }

        public sealed class DisposableOtherViewModel : ViewModelBase
        {
            public bool Disposed { get; private set; }
            protected override void OnDispose() => Disposed = true;
        }

        [Test]
        public async Task Back_ScreenShownOverOpenPopupView_PopupAnswersFirst()
        {
            await _ui.Navigate<ScreenA>(() => new FakeViewModel());
            await _ui.Show<PopupA>(new FakeViewModel());
            await _ui.Navigate<ScreenB>(() => new FakeViewModel());

            var consumed = _ui.Back();

            Assert.IsTrue(consumed);
            Assert.AreEqual(ViewState.Hidden, _ui.Get<PopupA>().State);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenB>().State);
            Assert.IsTrue(_ui.CanNavigateBack);
        }

        [Test]
        public async Task Back_HandlerPushedBeforeScreen_RunsBeforeScreen()
        {
            await _ui.Navigate<ScreenA>(() => new FakeViewModel());
            var handled = false;
            using var handle = _ui.PushBackHandler(() => handled = true);
            await _ui.Navigate<ScreenB>(() => new FakeViewModel());

            var consumed = _ui.Back();

            Assert.IsTrue(consumed);
            Assert.IsTrue(handled);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenB>().State);
        }
    }
}
