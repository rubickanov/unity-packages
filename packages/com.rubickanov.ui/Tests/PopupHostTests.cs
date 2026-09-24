using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

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

        // A button off any panel gets no events: run its click handlers directly.
        private static void Click(Button button) =>
            typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(button.clickable, new object?[] { null });

        [Test]
        public async Task Back_PopupViewThenEscapePopup_ClosesPopupThenViewThenReturnsFalse()
        {
            await _ui.Register<PopupA>();
            var view = _popups.ShowView<PopupA>(new FakeViewModel());
            var popup = _popups.Create().Message("hint").CloseOn(PopupCloseTriggers.Escape).Open();

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
        public void Close_DuringHideAnimation_NothingInPanelPickable()
        {
            var animation = new ControlledAnimation();
            var popup = _popups.Create().Message("choose").Button("OK", "ok").Button("Cancel", "cancel")
                .Input().Animation(animation).Open();
            animation.CompleteShow();

            popup.Close();
            var panel = PopupLayer.Q(className: PopupStyle.Panel);

            Assert.IsNotNull(panel);
            Assert.IsTrue(panel.Query<VisualElement>().ToList().All(e => e.pickingMode == PickingMode.Ignore));
        }

        [Test]
        public void CreateDialog_Modal_OnOverlayLayerLikeModalPopups()
        {
            _popups.CreateDialog("Abandon ship?").Button("Yes", "yes").Open();

            Assert.IsNotNull(_root.Q("overlay-layer").Q(className: PopupStyle.Dialog));
            Assert.IsNull(PopupLayer.Q(className: PopupStyle.Dialog));
        }

        [Test]
        public async Task ShowConfirm_ConfirmClicked_True()
        {
            var confirm = _popups.ShowConfirm("Abandon ship?", "The crew stays behind.");
            var button = _root.Q("overlay-layer").Query<Button>(className: PopupStyle.ButtonPrimary).First();

            Click(button);

            Assert.IsTrue(await confirm);
        }

        [Test]
        public async Task ShowConfirm_Back_False()
        {
            var confirm = _popups.ShowConfirm("Abandon ship?", "The crew stays behind.");

            _ui.Back();

            Assert.IsFalse(await confirm);
        }

        [Test]
        public void ShowModal_DisposeHandle_Closes()
        {
            var modal = _popups.ShowModal("Saving", "Please wait");
            var capturedWhileOpen = _ui.PointerCaptured.CurrentValue;

            modal.Dispose();

            Assert.IsTrue(capturedWhileOpen);
            Assert.IsFalse(modal.IsOpen);
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
            Assert.IsFalse(_ui.Back());
        }

        [Test]
        public async Task Button_Clicked_ClosesWithItsId()
        {
            var popup = _popups.Create().Message("choose").Button("Later", "later").Open();

            Click(PopupLayer.Q<Button>(className: PopupStyle.Button));

            var result = await popup.Result;
            Assert.AreEqual("later", result.ButtonId);
            Assert.AreEqual(PopupCloseReason.Button, result.Reason);
        }

        [Test]
        public void CloseButton_Text_IsMultiplicationSign()
        {
            _popups.Create().Message("info").CloseOn(PopupCloseTriggers.CloseButton).Open();

            Assert.AreEqual("\u00D7", PopupLayer.Q<Button>(className: PopupStyle.Close).text);
        }

        [Test]
        public void SetTitleAndMessage_OpenPopup_ChangesLabels()
        {
            var popup = _popups.Create().Title("Loading").Message("0%").Open();

            popup.SetTitle("Loaded");
            popup.SetMessage("100%");

            Assert.AreEqual("Loaded", PopupLayer.Q<Label>(className: PopupStyle.Title).text);
            Assert.AreEqual("100%", PopupLayer.Q<Label>(className: PopupStyle.Message).text);
            Assert.AreSame(PopupLayer.Q(className: PopupStyle.Panel), popup.Panel);
        }

        [Test]
        public void Fill_Placement_PanelStretchedOverLayer()
        {
            var popup = (PopupInstance)_popups.Create().Message("full").At(PopupPlacement.Fill()).Open();

            popup.Reposition();

            var style = popup.Panel.style;
            Assert.AreEqual(0f, style.left.value.value);
            Assert.AreEqual(0f, style.top.value.value);
            Assert.AreEqual(0f, style.right.value.value);
            Assert.AreEqual(0f, style.bottom.value.value);
        }

        [Test]
        public void SetPlacement_FromFill_ReleasesRightAndBottom()
        {
            var popup = (PopupInstance)_popups.Create().Message("full").At(PopupPlacement.Fill()).Open();
            popup.Reposition();

            popup.SetPlacement(PopupPlacement.ScreenCenter());

            Assert.AreEqual(StyleKeyword.Null, popup.Panel.style.right.keyword);
            Assert.AreEqual(StyleKeyword.Null, popup.Panel.style.bottom.keyword);
        }

        [Test]
        public void RepositionFollowers_OneFollowerThrows_OthersStillMove()
        {
            var cameraObject = new GameObject("camera");
            var anchor = new GameObject("anchor");
            using var host = new PopupHost(_root, _ui,
                pointerScreenPosition: () => throw new InvalidOperationException("pointer"));
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                anchor.transform.position = new Vector3(0f, 0f, 10f);
                var marker = host.Create().Message("marker")
                    .At(PopupPlacement.AtWorld(anchor.transform, camera: camera)).Open();
                host.Create().Message("cursor").At(PopupPlacement.Cursor()).Open();
                LogAssert.Expect(LogType.Exception, new Regex("pointer"));

                host.RepositionFollowers();

                Assert.AreNotEqual(StyleKeyword.Null, marker.Panel.style.left.keyword);
                Assert.AreEqual(2, host.FollowerCount);
            }
            finally
            {
                Object.DestroyImmediate(anchor);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Reposition_WorldAnchorBehindCamera_ReleasesInputUntilBackInView()
        {
            var cameraObject = new GameObject("camera");
            var anchor = new GameObject("anchor");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                anchor.transform.position = new Vector3(0f, 0f, -10f);
                var popup = (PopupInstance)_popups.Create().Message("marker").Modal()
                    .CloseOn(PopupCloseTriggers.Escape)
                    .At(PopupPlacement.AtWorld(anchor.transform, camera: camera)).Open();
                var backdrop = _root.Q(className: PopupStyle.Backdrop);

                popup.Reposition();
                var capturedWhileHidden = _ui.PointerCaptured.CurrentValue;
                var backdropWhileHidden = backdrop.style.display.value;
                var backWhileHidden = _ui.Back();
                anchor.transform.position = new Vector3(0f, 0f, 10f);
                popup.Reposition();

                Assert.IsFalse(capturedWhileHidden);
                Assert.AreEqual(DisplayStyle.None, backdropWhileHidden);
                Assert.IsFalse(backWhileHidden);
                Assert.IsTrue(popup.IsOpen);
                Assert.IsTrue(_ui.PointerCaptured.CurrentValue);
                Assert.AreEqual(DisplayStyle.Flex, backdrop.style.display.value);
                Assert.IsTrue(_ui.Back());
                Assert.IsFalse(popup.IsOpen);
            }
            finally
            {
                Object.DestroyImmediate(anchor);
                Object.DestroyImmediate(cameraObject);
            }
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

        [Test]
        public void SetPlacement_ClosedPopup_DoesNotStartFollowing()
        {
            var popup = _popups.Create().Message("info").Open();
            popup.Close();

            popup.SetPlacement(PopupPlacement.Cursor());

            Assert.AreEqual(0, _popups.FollowerCount);
        }

        [Test]
        public async Task Reposition_WorldAnchorDestroyed_ClosesAndReleasesPointer()
        {
            var anchor = new GameObject("anchor");
            var popup = _popups.Create().Message("marker").Button("OK", "ok")
                .At(PopupPlacement.AtWorld(anchor.transform)).Open();
            Object.DestroyImmediate(anchor);

            ((PopupInstance)popup).Reposition();

            Assert.IsFalse(popup.IsOpen);
            Assert.AreEqual(PopupCloseReason.AnchorDestroyed, (await popup.Result).Reason);
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
            Assert.AreEqual(0, _popups.FollowerCount);
        }

        [Test]
        public async Task Open_DismissOthers_ClosesOthersAsReplaced()
        {
            var first = _popups.Create().Message("first").Open();

            _popups.Create().Message("second").DismissOthers().Open();

            Assert.AreEqual(PopupCloseReason.Replaced, (await first.Result).Reason);
        }

        [Test]
        public void Builder_Modal_OnOverlayLayer()
        {
            _popups.Create().Message("modal").Modal().Open();

            Assert.IsNotNull(_root.Q("overlay-layer").Q(className: PopupStyle.Modal));
        }

        [Test]
        public void Builder_OnLayerThenModal_KeepsNamedLayer()
        {
            _popups.Create().Message("modal").OnLayer(UILayer.Popup).Modal().Open();

            Assert.IsNotNull(PopupLayer.Q(className: PopupStyle.Modal));
            Assert.IsNull(_root.Q("overlay-layer").Q(className: PopupStyle.Modal));
        }

        [Test]
        public void Tooltip_OnOverlayLayerAboveDialogs()
        {
            var tooltip = _popups.Create();
            TooltipExtensions.Configure(tooltip, new VisualElement(), popup => popup.Message("hint"));

            tooltip.Open();

            Assert.IsNotNull(_root.Q("overlay-layer").Q(className: PopupStyle.Tooltip));
        }

        [Test]
        public void Dispose_DefaultStyleSheet_RemovedFromRoot()
        {
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            var host = new PopupHost(_root, _ui, sheet);

            host.Dispose();

            Assert.IsFalse(_root.styleSheets.Contains(sheet));
            Object.DestroyImmediate(sheet);
        }
    }
}
