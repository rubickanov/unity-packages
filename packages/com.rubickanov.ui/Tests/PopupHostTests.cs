using System;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine;
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
        private VisualElement OverlayLayer => _root.Q("overlay-layer");

        private PopupBuilder Popup() => _popups.Create().Content(() => new Label("hint"));

        // ── Back ─────────────────────────────────────────────────

        [Test]
        public async Task Back_PopupViewThenEscapePopup_ClosesPopupThenViewThenReturnsFalse()
        {
            await _ui.Register<PopupA>();
            var view = _popups.ShowView<PopupA, FakeViewModel>(new FakeViewModel());
            var popup = Popup().CloseOn(PopupCloseTriggers.Escape).Open();

            var first = _ui.Back();
            var popupOpenAfterFirst = popup.IsOpen;
            var viewOpenAfterFirst = view.IsOpen;
            var second = _ui.Back();
            var third = _ui.Back();

            Assert.IsTrue(first);
            Assert.IsFalse(popupOpenAfterFirst);
            Assert.IsTrue(viewOpenAfterFirst);
            Assert.AreEqual(PopupCloseReason.Escape, (await popup.Result).Reason);
            Assert.IsTrue(second);
            Assert.IsFalse(view.IsOpen);
            Assert.IsFalse(third);
        }

        [Test]
        public void Back_PassiveWithoutEscape_NotConsumed()
        {
            var popup = Popup().Open();

            var consumed = _ui.Back();

            Assert.IsFalse(consumed);
            Assert.IsTrue(popup.IsOpen);
        }

        [Test]
        public void Back_ModalWithoutEscape_ConsumedAndStaysOpen()
        {
            var calls = 0;
            using var under = _ui.PushBackHandler(() => ++calls > 0);
            var modal = Popup().Modal().Open();

            var consumed = _ui.Back();

            Assert.IsTrue(consumed);
            Assert.IsTrue(modal.IsOpen);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Back_ModalClosed_ReachesHandlerUnder()
        {
            var calls = 0;
            using var under = _ui.PushBackHandler(() => ++calls > 0);
            Popup().Modal().Open().Close();

            _ui.Back();

            Assert.AreEqual(1, calls);
        }

        // ── Pointer and picking ──────────────────────────────────

        [Test]
        public void Open_ModalPopup_CapturesPointerUntilClose()
        {
            var popup = Popup().Modal().Open();
            var capturedWhileOpen = _ui.PointerCaptured.CurrentValue;

            popup.Close();

            Assert.IsTrue(capturedWhileOpen);
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public void Open_PassiveWithContentFactory_PanelPickableAndPointerCaptured()
        {
            var button = new Button();
            var popup = _popups.Create().Content(() => button).Open();

            Assert.AreEqual(PickingMode.Position, popup.Panel.pickingMode);
            Assert.AreEqual(PickingMode.Position, button.pickingMode);
            Assert.IsTrue(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public void Open_PassThrough_NothingPickableAndPointerFree()
        {
            var popup = Popup().PassThrough().Open();

            Assert.IsTrue(popup.Panel.Query<VisualElement>().ToList().All(e => e.pickingMode == PickingMode.Ignore));
            Assert.AreEqual(PickingMode.Ignore, popup.Panel.parent.pickingMode);
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public void Open_Modal_BackdropBlocksAndCoversLayer()
        {
            var popup = Popup().Modal().Open();
            var backdrop = popup.Panel.parent;

            Assert.IsTrue(backdrop.ClassListContains(PopupStyle.Backdrop));
            Assert.AreEqual(PickingMode.Position, backdrop.pickingMode);
            Assert.AreEqual(Position.Absolute, backdrop.style.position.value);
            Assert.AreEqual(0f, backdrop.style.right.value.value);
            Assert.AreEqual(0f, backdrop.style.bottom.value.value);
        }

        [Test]
        public void Open_ContentFactoryReturnsNull_ThrowsAndLeavesNothing()
        {
            Assert.Throws<InvalidOperationException>(() => _popups.Create().Content(() => null!).Open());

            Assert.AreEqual(0, PopupLayer.childCount);
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
        }

        // ── Close ────────────────────────────────────────────────

        [Test]
        public async Task Close_WithId_ResultCarriesIdAndCode()
        {
            var popup = Popup().Open();

            popup.Close("yes");

            var result = await popup.Result;
            Assert.AreEqual("yes", result.Id);
            Assert.AreEqual(PopupCloseReason.Code, result.Reason);
        }

        [Test]
        public async Task Close_DuringShowAnimation_CompletesResultOnceAndLeavesNoElements()
        {
            var animation = new ControlledAnimation();
            var popup = Popup().Modal().Animation(animation).Open();
            var completions = 0;
            popup.Result.ContinueWith(_ => completions++).Forget();

            popup.Close("first");
            popup.Close("second");
            var resultCompletedBeforeHide = popup.Result.Status == UniTaskStatus.Succeeded;
            animation.CompleteHide();

            Assert.IsTrue(resultCompletedBeforeHide);
            Assert.AreEqual(1, completions);
            Assert.AreEqual("first", (await popup.Result).Id);
            Assert.AreEqual(0, PopupLayer.childCount + OverlayLayer.childCount);
        }

        [Test]
        public void Close_WithHideAnimation_ElementsRemovedAfterHide()
        {
            var animation = new ControlledAnimation();
            var popup = Popup().Animation(animation).Open();
            animation.CompleteShow();

            popup.Close();
            var childrenDuringHide = PopupLayer.childCount;
            animation.CompleteHide();

            Assert.AreEqual(1, childrenDuringHide);
            Assert.AreEqual(0, PopupLayer.childCount);
        }

        [Test]
        public void Close_DuringHideAnimation_NothingInPanelPickable()
        {
            var animation = new ControlledAnimation();
            var popup = _popups.Create().Content(() =>
            {
                var buttons = new VisualElement();
                buttons.Add(new Button());
                buttons.Add(new TextField());
                return buttons;
            }).Modal().Animation(animation).Open();
            animation.CompleteShow();

            popup.Close();

            Assert.IsTrue(popup.Panel.Query<VisualElement>().ToList().All(e => e.pickingMode == PickingMode.Ignore));
            Assert.AreEqual(PickingMode.Ignore, popup.Panel.parent.pickingMode);
        }

        [Test]
        public void Dispose_Handle_ClosesPopup()
        {
            var popup = Popup().Modal().Open();

            popup.Dispose();

            Assert.IsFalse(popup.IsOpen);
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public async Task CloseAll_TwoPopups_ClosesBothAsCode()
        {
            var first = Popup().Open();
            var second = Popup().Modal().Open();

            _popups.CloseAll();

            Assert.AreEqual(PopupCloseReason.Code, (await first.Result).Reason);
            Assert.AreEqual(PopupCloseReason.Code, (await second.Result).Reason);
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public async Task Open_DismissOthers_ClosesOthersAsReplaced()
        {
            var first = Popup().Open();

            Popup().DismissOthers().Open();

            Assert.AreEqual(PopupCloseReason.Replaced, (await first.Result).Reason);
        }

        // ── Placement ────────────────────────────────────────────

        [Test]
        public void Fill_Placement_PanelStretchedOverLayer()
        {
            var popup = Popup().At(PopupPlacement.Fill()).Open();

            var style = popup.Panel.style;
            Assert.AreEqual(Position.Absolute, style.position.value);
            Assert.AreEqual(0f, style.left.value.value);
            Assert.AreEqual(0f, style.top.value.value);
            Assert.AreEqual(0f, style.right.value.value);
            Assert.AreEqual(0f, style.bottom.value.value);
        }

        [Test]
        public void Screen_TopRight_FrameAlignsPanelToCornerInsetByOffset()
        {
            var popup = Popup().At(PopupPlacement.Screen(PopupAnchorCorner.TopRight, new Vector2(16f, 8f))).Open();

            var frame = popup.Panel.parent.style;
            Assert.AreEqual(Justify.FlexStart, frame.justifyContent.value);
            Assert.AreEqual(Align.FlexEnd, frame.alignItems.value);
            Assert.AreEqual(16f, frame.paddingRight.value.value);
            Assert.AreEqual(8f, frame.paddingTop.value.value);
        }

        [Test]
        public void ScreenCenter_WithOffset_PanelMovedFromTheMiddle()
        {
            var popup = Popup().At(PopupPlacement.ScreenCenter(new Vector2(0f, -40f))).Open();

            var frame = popup.Panel.parent.style;
            Assert.AreEqual(Justify.Center, frame.justifyContent.value);
            Assert.AreEqual(Align.Center, frame.alignItems.value);
            Assert.AreEqual(-40f, popup.Panel.style.top.value.value);
        }

        // ── Layers and animation ─────────────────────────────────

        [Test]
        public void Builder_Modal_OnOverlayLayer()
        {
            Popup().Modal().Open();

            Assert.IsNotNull(OverlayLayer.Q(className: PopupStyle.Modal));
        }

        [Test]
        public void Builder_OnLayerThenModal_KeepsNamedLayer()
        {
            Popup().OnLayer(UILayer.Popup).Modal().Open();

            Assert.IsNotNull(PopupLayer.Q(className: PopupStyle.Modal));
            Assert.IsNull(OverlayLayer.Q(className: PopupStyle.Modal));
        }

        [Test]
        public void Builder_ClassAndStyle_OnPanel()
        {
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();

            var popup = Popup().Class("toast").Style(sheet).Open();

            Assert.IsTrue(popup.Panel.ClassListContains("toast"));
            Assert.IsTrue(popup.Panel.styleSheets.Contains(sheet));
            UnityEngine.Object.DestroyImmediate(sheet);
        }

        [Test]
        public void Open_DefaultAnimationFromHost_PlaysShow()
        {
            var animation = new ControlledAnimation();
            using var host = new PopupHost(_root, _ui, animation);

            host.Create().Content(() => new Label("info")).Open();

            Assert.AreEqual(1, animation.Shows.Count);
        }

        [Test]
        public void Dispose_OpenPopup_RemovedWithoutHideAnimation()
        {
            var animation = new ControlledAnimation();
            var host = new PopupHost(_root, _ui, animation);
            host.Create().Content(() => new Label("info")).Open();

            host.Dispose();

            Assert.AreEqual(0, PopupLayer.childCount);
            Assert.AreEqual(0, animation.Hides.Count);
        }

        // ── Another IUIService ───────────────────────────────────

        [Test]
        public void Open_HostOverAnotherUIService_CapturesAndPushesBackThroughIt()
        {
            var ui = new CountingUIService();
            using var host = new PopupHost(_root, ui);

            var popup = host.Create().Content(() => new Label("modal")).Modal().Open();
            var capturesWhileOpen = ui.Captures;
            var handlersWhileOpen = ui.BackHandlers;
            popup.Close();

            Assert.AreEqual(1, capturesWhileOpen);
            Assert.AreEqual(1, handlersWhileOpen);
            Assert.AreEqual(0, ui.Captures);
            Assert.AreEqual(0, ui.BackHandlers);
        }

        [Test]
        public void Open_ViewContentOverAnotherUIService_ThrowsNamingTheView()
        {
            using var host = new PopupHost(_root, new CountingUIService());

            var ex = Assert.Throws<InvalidOperationException>(() =>
                host.ShowView<PopupA, FakeViewModel>(new FakeViewModel()));

            StringAssert.Contains(nameof(PopupA), ex.Message);
        }

        /// <summary>An <see cref="IUIService"/> of a game's own: counts what popups take from it.</summary>
        private sealed class CountingUIService : IUIService
        {
            private readonly ReactiveProperty<bool> _captured = new(false);
            public int Captures;
            public int BackHandlers;

            public ReadOnlyReactiveProperty<bool> PointerCaptured => _captured;
            public bool CanNavigateBack => false;

            public IDisposable CapturePointer()
            {
                Captures++;
                return Disposable.Create(() => Captures--);
            }

            public IDisposable PushBackHandler(Func<bool> handler)
            {
                BackHandlers++;
                return Disposable.Create(() => BackHandlers--);
            }

            public UniTask Register<T>() where T : View => UniTask.CompletedTask;
            public void Unregister<T>() where T : View { }
            public T Get<T>() where T : View => throw new NotSupportedException();

            public UniTask Show<TView, TViewModel>(TViewModel viewModel)
                where TView : View<TViewModel> where TViewModel : ViewModelBase => UniTask.CompletedTask;

            public UniTask Navigate<TView, TViewModel>(Func<TViewModel> createViewModel)
                where TView : View<TViewModel> where TViewModel : ViewModelBase => UniTask.CompletedTask;

            public void Hide<T>() where T : View { }
            public UniTask HideAsync<T>() where T : View => UniTask.CompletedTask;
            public bool Back() => false;
        }
    }
}
