using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class PopupHostTests
    {
        private VisualElement _root = null!;
        private UIService _ui = null!;
        private PopupHost _popups = null!;

        [SetUp]
        public void SetUp()
        {
            _root = TestRoot.Create();
            _ui = new UIService(_root, new RecordingUxmlLoader().Load);
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
        public async Task Back_PopupViewThenEscapePopup_ClosesPopupThenViewThenReturnsFalse()
        {
            await _ui.Register<PopupA>();
            var view = _ui.Get<PopupA>();
            await _ui.Show<PopupA>(new FakeViewModel());
            var popup = _popups.Create().Message("hint").CloseOn(PopupCloseTriggers.Escape).Open();

            var first = _ui.Back();
            var popupOpenAfterFirst = popup.IsOpen;
            var viewStateAfterFirst = view.State;
            var second = _ui.Back();
            var third = _ui.Back();

            Assert.IsTrue(first);
            Assert.IsFalse(popupOpenAfterFirst);
            Assert.AreEqual(ViewState.Shown, viewStateAfterFirst);
            Assert.AreEqual(PopupCloseReason.Escape, (await popup.Result).Reason);
            Assert.IsTrue(second);
            Assert.AreEqual(ViewState.Hidden, view.State);
            Assert.IsFalse(third);
        }

        [Test]
        public void Open_ModalPopup_CapturesPointerUntilClose()
        {
            var popup = _popups.Create().Message("modal").Modal().Open();
            var capturedWhileOpen = _ui.PointerCaptured.CurrentValue;

            popup.Close();

            Assert.IsTrue(capturedWhileOpen);
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public void Open_PopupWithButton_CapturesPointer()
        {
            _popups.Create().Message("choose").Button("OK", "ok").Open();

            Assert.IsTrue(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public void Open_PassiveInfoPopup_DoesNotCapturePointer()
        {
            _popups.Create().Message("info").Open();

            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public void Open_WithoutEscapeTrigger_BackNotConsumed()
        {
            var popup = _popups.Create().Message("info").Open();

            var consumed = _ui.Back();

            Assert.IsFalse(consumed);
            Assert.IsTrue(popup.IsOpen);
        }

        [Test]
        public async Task Close_DuringShowAnimation_CompletesResultOnceAndLeavesNoElements()
        {
            var animation = new ControlledAnimation();
            var popup = _popups.Create().Message("modal").Modal().Animation(animation).Open();
            var completions = 0;
            popup.Result.ContinueWith(_ => completions++).Forget();

            popup.Close("first");
            popup.Close("second");
            var resultCompletedBeforeHide = popup.Result.Status == UniTaskStatus.Succeeded;
            animation.CompleteHide();

            Assert.IsTrue(resultCompletedBeforeHide);
            Assert.AreEqual(1, completions);
            Assert.AreEqual("first", (await popup.Result).ButtonId);
            Assert.AreEqual(0, PopupLayer.childCount + _root.Q("overlay-layer").childCount);
        }

        [Test]
        public void Close_WithHideAnimation_ElementsRemovedAfterHide()
        {
            var animation = new ControlledAnimation();
            var popup = _popups.Create().Message("info").Animation(animation).Open();
            animation.CompleteShow();

            popup.Close();
            var childrenDuringHide = PopupLayer.childCount;
            animation.CompleteHide();

            Assert.AreEqual(1, childrenDuringHide);
            Assert.AreEqual(0, PopupLayer.childCount);
        }

        [Test]
        public void Open_DefaultAnimationFromHost_PlaysShow()
        {
            var animation = new ControlledAnimation();
            using var host = new PopupHost(_root, _ui, animation: animation);

            host.Create().Message("info").Open();

            Assert.AreEqual(1, animation.Shows.Count);
        }

        [Test]
        public void Dispose_OpenPopup_RemovedWithoutHideAnimation()
        {
            var animation = new ControlledAnimation();
            var host = new PopupHost(_root, _ui, animation: animation);
            host.Create().Message("info").Open();

            host.Dispose();

            Assert.AreEqual(0, PopupLayer.childCount);
            Assert.AreEqual(0, animation.Hides.Count);
        }
    }
}
