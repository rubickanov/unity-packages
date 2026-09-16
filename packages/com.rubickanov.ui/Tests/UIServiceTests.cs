using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class UIServiceTests
    {
        private VisualElement _root = null!;
        private RecordingUxmlLoader _loader = null!;
        private UIService _ui = null!;

        [SetUp]
        public void SetUp()
        {
            _root = TestRoot.Create();
            _loader = new RecordingUxmlLoader();
            _ui = new UIService(_root, _loader.Load);
        }

        [TearDown]
        public void TearDown() => _ui?.Dispose();

        private async Task<T> Registered<T>(IViewAnimation? animation = null) where T : FakeView
        {
            await _ui.Register<T>();
            var view = _ui.Get<T>();
            view.TestAnimation = animation;
            return view;
        }

        // ── Registration ─────────────────────────────────────────

        [Test]
        public void Ctor_RootWithoutLayers_ThrowsInvalidOperation()
        {
            Assert.Throws<InvalidOperationException>(() => new UIService(new VisualElement(), _loader.Load));
        }

        [Test]
        public void Get_Unregistered_ThrowsInvalidOperation()
        {
            Assert.Throws<InvalidOperationException>(() => _ui.Get<ScreenA>());
        }

        [Test]
        public async Task Register_PopupView_AttachedToPopupLayerAndHidden()
        {
            await _ui.Register<PopupA>();

            var view = _ui.Get<PopupA>();

            Assert.AreSame(_root.Q("popup-layer"), view.Root.parent);
            Assert.AreEqual(ViewState.Hidden, view.State);
            Assert.AreEqual(DisplayStyle.None, view.Root.style.display.value);
        }

        [Test]
        public async Task Register_HudView_AttachedToHudLayer()
        {
            await _ui.Register<HudA>();

            Assert.AreSame(_root.Q("hud-layer"), _ui.Get<HudA>().Root.parent);
        }

        [Test]
        public async Task Register_CodeOnlyView_DoesNotLoadUxml()
        {
            await _ui.Register<ScreenA>();

            CollectionAssert.IsEmpty(_loader.Loaded);
        }

        [Test]
        public async Task Register_ViewWithUxml_LoadsByViewTypeName()
        {
            await _ui.Register<UxmlScreen>();

            CollectionAssert.AreEqual(new[] { nameof(UxmlScreen) }, _loader.Loaded);
            CollectionAssert.IsEmpty(_loader.Released);
        }

        [Test]
        public async Task Register_AlreadyRegistered_ThrowsAndKeepsFirstView()
        {
            await _ui.Register<ScreenA>();
            var first = _ui.Get<ScreenA>();

            Assert.ThrowsAsync<InvalidOperationException>(async () => await _ui.Register<ScreenA>());

            Assert.AreSame(first, _ui.Get<ScreenA>());
            Assert.AreEqual(1, _root.Q("screen-layer").childCount);
        }

        [Test]
        public async Task Register_WhileSameTypeLoading_Throws()
        {
            _loader.Deferred = true;
            var first = _ui.Register<UxmlScreen>();

            Assert.ThrowsAsync<InvalidOperationException>(async () => await _ui.Register<UxmlScreen>());

            _loader.Complete(nameof(UxmlScreen));
            await first;
            CollectionAssert.AreEqual(new[] { nameof(UxmlScreen) }, _loader.Loaded);
            Assert.DoesNotThrow(() => _ui.Get<UxmlScreen>());
        }

        [Test]
        public void Unregister_WhileLoading_RegisterThrowsCanceledAndReleasesUxml()
        {
            _loader.Deferred = true;
            var registration = _ui.Register<UxmlScreen>();

            _ui.Unregister<UxmlScreen>();
            _loader.Complete(nameof(UxmlScreen));

            Assert.CatchAsync<OperationCanceledException>(async () => await registration);
            Assert.Throws<InvalidOperationException>(() => _ui.Get<UxmlScreen>());
            CollectionAssert.AreEqual(new[] { nameof(UxmlScreen) }, _loader.Released);
            Assert.AreEqual(0, _root.Q("screen-layer").childCount);
        }

        // ── Show and view model type ─────────────────────────────

        [Test]
        public async Task ShowScreen_NoActive_BindsAndShows()
        {
            var a = await Registered<ScreenA>();
            var vm = new FakeViewModel();

            await _ui.Show<ScreenA>(vm);

            Assert.AreEqual(1, a.BindCalls);
            Assert.AreSame(vm, a.LastViewModel);
            Assert.AreEqual(ViewState.Shown, a.State);
            Assert.AreSame(a, _ui.DebugActiveScreen);
        }

        [Test]
        public async Task Show_WrongViewModelType_ThrowsBeforeAnyStateChange()
        {
            var a = await Registered<PopupA>();
            var b = await Registered<PopupB>();
            var vm = new FakeViewModel();
            await _ui.Show<PopupA>(vm);

            var ex = Assert.ThrowsAsync<ArgumentException>(async () => await _ui.Show<PopupA>(new OtherViewModel()));

            StringAssert.Contains(nameof(PopupA), ex.Message);
            StringAssert.Contains(nameof(FakeViewModel), ex.Message);
            StringAssert.Contains(nameof(OtherViewModel), ex.Message);
            Assert.ThrowsAsync<ArgumentException>(async () => await _ui.Show<PopupB>(new OtherViewModel()));
            Assert.AreEqual(ViewState.Shown, a.State);
            Assert.AreEqual(1, a.BindCalls);
            Assert.AreEqual(0, vm.DisposeCalls);
            Assert.AreEqual(ViewState.Hidden, b.State);
            Assert.AreEqual(0, b.BindCalls);
            CollectionAssert.AreEqual(new[] { a }, _ui.DebugPopupStack);
        }

        [Test]
        public async Task Show_SameViewModelTwice_BindsOnceAndDoesNotDispose()
        {
            var a = await Registered<PopupA>();
            var vm = new FakeViewModel();

            await _ui.Show<PopupA>(vm);
            await _ui.Show<PopupA>(vm);

            Assert.AreEqual(1, a.BindCalls);
            Assert.AreEqual(0, a.UnbindCalls);
            Assert.AreEqual(0, vm.DisposeCalls);
            Assert.AreEqual(ViewState.Shown, a.State);
        }

        [Test]
        public async Task ShowPopup_AlreadyShownWithOtherViewModel_RebindsAndDisposesOld()
        {
            var a = await Registered<PopupA>();
            var firstVm = new FakeViewModel();
            var secondVm = new FakeViewModel();

            await _ui.Show<PopupA>(firstVm);
            await _ui.Show<PopupA>(secondVm);

            Assert.AreEqual(2, a.BindCalls);
            Assert.AreEqual(1, a.UnbindCalls);
            Assert.AreSame(secondVm, a.LastViewModel);
            Assert.AreEqual(1, firstVm.DisposeCalls);
            Assert.AreEqual(0, secondVm.DisposeCalls);
            Assert.AreEqual(ViewState.Shown, a.State);
        }

        [Test]
        public async Task ShowPopup_AlreadyShown_MovesToTopOnce()
        {
            var a = await Registered<PopupA>();
            var b = await Registered<PopupB>();

            await _ui.Show<PopupA>(new FakeViewModel());
            await _ui.Show<PopupB>(new FakeViewModel());
            await _ui.Show<PopupA>(new FakeViewModel());

            CollectionAssert.AreEqual(new FakeView[] { b, a }, _ui.DebugPopupStack);
        }

        [Test]
        public async Task ShowScreen_BindThrows_RollsBackAndDisposesViewModel()
        {
            var a = await Registered<ScreenA>();
            a.ThrowOnBind = new InvalidOperationException("boom");
            var vm = new FakeViewModel();

            Assert.ThrowsAsync<InvalidOperationException>(async () => await _ui.Show<ScreenA>(vm));

            Assert.AreEqual(ViewState.Hidden, a.State);
            Assert.IsNull(_ui.DebugActiveScreen);
            Assert.AreEqual(1, vm.DisposeCalls);
        }

        [Test]
        public async Task ShowScreen_BindThrows_PreviousScreenStaysActive()
        {
            var a = await Registered<ScreenA>();
            var b = await Registered<ScreenB>();
            await _ui.Show<ScreenA>(new FakeViewModel());
            b.ThrowOnBind = new InvalidOperationException("boom");

            Assert.ThrowsAsync<InvalidOperationException>(async () => await _ui.Show<ScreenB>(new FakeViewModel()));

            Assert.AreEqual(ViewState.Shown, a.State);
            Assert.AreSame(a, _ui.DebugActiveScreen);
        }

        [Test]
        public async Task ShowPopup_AnimationThrows_HiddenAndRemovedFromStack()
        {
            var animation = new ControlledAnimation();
            var a = await Registered<PopupA>(animation);
            var vm = new FakeViewModel();
            var show = _ui.Show<PopupA>(vm);

            animation.FailShow(new InvalidOperationException("boom"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await show);
            CollectionAssert.IsEmpty(_ui.DebugPopupStack);
            Assert.AreEqual(ViewState.Hidden, a.State);
            Assert.AreEqual(1, vm.DisposeCalls);
        }

        // ── Transitions (F1, F2, D4) ─────────────────────────────

        [Test]
        public async Task ShowPopup_DuringHide_ShownWithSecondViewModelAndFirstDisposedOnce()
        {
            var animation = new ControlledAnimation();
            var a = await Registered<PopupA>(animation);
            var firstVm = new FakeViewModel();
            var secondVm = new FakeViewModel();
            var firstShow = _ui.Show<PopupA>(firstVm);
            animation.CompleteShow();
            await firstShow;

            var hide = _ui.HideAsync<PopupA>();
            var secondShow = _ui.Show<PopupA>(secondVm);

            Assert.AreEqual(ViewState.Showing, a.State);
            Assert.AreSame(secondVm, a.LastViewModel);
            Assert.AreEqual(1, firstVm.DisposeCalls);
            CollectionAssert.AreEqual(new[] { a }, _ui.DebugPopupStack);

            animation.Hides[0].TrySetResult();
            animation.CompleteShow();
            await hide;
            await secondShow;

            Assert.AreEqual(ViewState.Shown, a.State);
            Assert.AreEqual(DisplayStyle.Flex, a.Root.style.display.value);
            Assert.AreEqual(1, firstVm.DisposeCalls);
            Assert.AreEqual(0, secondVm.DisposeCalls);
            CollectionAssert.AreEqual(new[] { a }, _ui.DebugPopupStack);
        }

        [Test]
        public async Task ShowPopup_DuringHideWithSameViewModel_KeepsBindingAndShows()
        {
            var animation = new ControlledAnimation();
            var a = await Registered<PopupA>(animation);
            var vm = new FakeViewModel();
            var firstShow = _ui.Show<PopupA>(vm);
            animation.CompleteShow();
            await firstShow;

            var hide = _ui.HideAsync<PopupA>();
            var secondShow = _ui.Show<PopupA>(vm);
            animation.CompleteShow();
            await hide;
            await secondShow;

            Assert.AreEqual(ViewState.Shown, a.State);
            Assert.AreEqual(1, a.BindCalls);
            Assert.AreEqual(0, vm.DisposeCalls);
        }

        [Test]
        public async Task HideAsync_DuringShow_StopsInterceptingAtOnceThenHidesAndDisposes()
        {
            var animation = new ControlledAnimation();
            var a = await Registered<PopupA>(animation);
            var vm = new FakeViewModel();
            var show = _ui.Show<PopupA>(vm);

            var hide = _ui.HideAsync<PopupA>();

            Assert.AreEqual(ViewState.Hiding, a.State);
            Assert.IsFalse(a.IsVisible);
            Assert.AreEqual(PickingMode.Ignore, a.Root.pickingMode);
            CollectionAssert.IsEmpty(_ui.DebugPopupStack);
            Assert.AreEqual(0, vm.DisposeCalls);

            await show;
            animation.CompleteHide();
            await hide;

            Assert.AreEqual(ViewState.Hidden, a.State);
            Assert.AreEqual(DisplayStyle.None, a.Root.style.display.value);
            Assert.AreEqual(1, vm.DisposeCalls);
        }

        [Test]
        public async Task Hide_DuringShow_HiddenAtOnce()
        {
            var animation = new ControlledAnimation();
            var a = await Registered<PopupA>(animation);
            var vm = new FakeViewModel();
            var show = _ui.Show<PopupA>(vm);

            _ui.Hide<PopupA>();
            await show;

            Assert.AreEqual(ViewState.Hidden, a.State);
            Assert.AreEqual(1, vm.DisposeCalls);
            CollectionAssert.IsEmpty(_ui.DebugPopupStack);
        }

        [Test]
        public async Task ShowScreen_BackToBackWithPendingShow_FirstHiddenSecondActive()
        {
            var animationA = new ControlledAnimation();
            var animationB = new ControlledAnimation();
            var a = await Registered<ScreenA>(animationA);
            var b = await Registered<ScreenB>(animationB);
            var vmA = new FakeViewModel();
            var vmB = new FakeViewModel();

            var showA = _ui.Show<ScreenA>(vmA);
            var showB = _ui.Show<ScreenB>(vmB);

            Assert.AreSame(b, _ui.DebugActiveScreen);
            Assert.AreEqual(ViewState.Hiding, a.State);
            Assert.AreEqual(ViewState.Showing, b.State);

            await showA;
            animationA.CompleteHide();
            animationB.CompleteShow();
            await showB;

            Assert.AreEqual(ViewState.Hidden, a.State);
            Assert.AreEqual(1, vmA.DisposeCalls);
            Assert.AreEqual(ViewState.Shown, b.State);
            Assert.AreEqual(0, vmB.DisposeCalls);
            Assert.AreSame(b, _ui.DebugActiveScreen);
        }

        [Test]
        public async Task ShowScreen_BackToBackAndBackAgain_FirstShownWithNewViewModel()
        {
            var animationA = new ControlledAnimation();
            var animationB = new ControlledAnimation();
            var a = await Registered<ScreenA>(animationA);
            var b = await Registered<ScreenB>(animationB);
            var vmA1 = new FakeViewModel();
            var vmA2 = new FakeViewModel();
            var vmB = new FakeViewModel();

            var showA1 = _ui.Show<ScreenA>(vmA1);
            var showB = _ui.Show<ScreenB>(vmB);
            var showA2 = _ui.Show<ScreenA>(vmA2);
            animationA.CompleteShow();
            animationB.CompleteHide();
            await showA1;
            await showB;
            await showA2;

            Assert.AreSame(a, _ui.DebugActiveScreen);
            Assert.AreEqual(ViewState.Shown, a.State);
            Assert.AreSame(vmA2, a.LastViewModel);
            Assert.AreEqual(1, vmA1.DisposeCalls);
            Assert.AreEqual(ViewState.Hidden, b.State);
            Assert.AreEqual(1, vmB.DisposeCalls);
        }

        // ── Layers (D2, F9, F14) ─────────────────────────────────

        [Test]
        public async Task ShowHud_NotOnStackAndDoesNotInterceptInput()
        {
            var hud = await Registered<HudA>();

            await _ui.Show<HudA>(new FakeViewModel());

            Assert.AreEqual(ViewState.Shown, hud.State);
            CollectionAssert.IsEmpty(_ui.DebugPopupStack);
            Assert.IsNull(_ui.DebugActiveScreen);
            Assert.AreEqual(PickingMode.Ignore, hud.Root.pickingMode);
        }

        [Test]
        public async Task ShowPopup_InterceptsInputByDefault()
        {
            var a = await Registered<PopupA>();

            await _ui.Show<PopupA>(new FakeViewModel());

            Assert.AreEqual(PickingMode.Position, a.Root.pickingMode);
        }

        [Test]
        public async Task HideTop_HudAndPopupShown_HidesPopupAndLeavesHud()
        {
            var hud = await Registered<HudA>();
            var popup = await Registered<PopupA>();
            await _ui.Show<HudA>(new FakeViewModel());
            await _ui.Show<PopupA>(new FakeViewModel());

            _ui.HideTop();
            _ui.HideTop();

            Assert.AreEqual(ViewState.Hidden, popup.State);
            Assert.AreEqual(ViewState.Shown, hud.State);
        }

        [Test]
        public async Task HideAll_HudShown_LeavesHud()
        {
            var hud = await Registered<HudA>();
            await Registered<ScreenA>();
            await Registered<PopupA>();
            await _ui.Show<HudA>(new FakeViewModel());
            await _ui.Show<ScreenA>(new FakeViewModel());
            await _ui.Show<PopupA>(new FakeViewModel());

            _ui.HideAll();

            Assert.AreEqual(ViewState.Shown, hud.State);
            Assert.AreEqual(ViewState.Hidden, _ui.Get<ScreenA>().State);
            Assert.AreEqual(ViewState.Hidden, _ui.Get<PopupA>().State);
        }

        [Test]
        public async Task HideAllAsync_HudShown_LeavesHud()
        {
            var hud = await Registered<HudA>();
            await Registered<PopupA>();
            await _ui.Show<HudA>(new FakeViewModel());
            await _ui.Show<PopupA>(new FakeViewModel());

            await _ui.HideAllAsync();

            Assert.AreEqual(ViewState.Shown, hud.State);
            Assert.AreEqual(ViewState.Hidden, _ui.Get<PopupA>().State);
        }

        [Test]
        public async Task ShowHud_VisibilityCallbackNotFired()
        {
            await Registered<HudA>();
            var events = new List<bool>();
            _ui.SetVisibilityCallback(events.Add);

            await _ui.Show<HudA>(new FakeViewModel());

            CollectionAssert.IsEmpty(events);
        }

        // ── Hide ─────────────────────────────────────────────────

        [Test]
        public void Hide_Unregistered_NoOp()
        {
            Assert.DoesNotThrow(() => _ui.Hide<ScreenA>());
        }

        [Test]
        public async Task Hide_ActiveScreen_ClearsActiveAndDisposesViewModel()
        {
            var a = await Registered<ScreenA>();
            var vm = new FakeViewModel();
            await _ui.Show<ScreenA>(vm);

            _ui.Hide<ScreenA>();

            Assert.AreEqual(ViewState.Hidden, a.State);
            Assert.IsNull(_ui.DebugActiveScreen);
            Assert.AreEqual(1, a.UnbindCalls);
            Assert.AreEqual(1, vm.DisposeCalls);
        }

        [Test]
        public async Task HideTop_EmptyStack_NoOp()
        {
            await Registered<PopupA>();

            Assert.DoesNotThrow(() => _ui.HideTop());
        }

        [Test]
        public async Task HideTop_RemovesTopmostPopup()
        {
            var a = await Registered<PopupA>();
            var b = await Registered<PopupB>();
            await _ui.Show<PopupA>(new FakeViewModel());
            await _ui.Show<PopupB>(new FakeViewModel());

            _ui.HideTop();

            Assert.AreEqual(ViewState.Shown, a.State);
            Assert.AreEqual(ViewState.Hidden, b.State);
            CollectionAssert.AreEqual(new[] { a }, _ui.DebugPopupStack);
        }

        [Test]
        public async Task HideTopAsync_RemovesTopmostAtOnceAndHidesAfterAnimation()
        {
            var animation = new ControlledAnimation();
            var a = await Registered<PopupA>(animation);
            var show = _ui.Show<PopupA>(new FakeViewModel());
            animation.CompleteShow();
            await show;

            var hide = _ui.HideTopAsync();

            CollectionAssert.IsEmpty(_ui.DebugPopupStack);
            Assert.AreEqual(ViewState.Hiding, a.State);
            animation.CompleteHide();
            await hide;
            Assert.AreEqual(ViewState.Hidden, a.State);
        }

        [Test]
        public async Task HideAll_ClearsScreenAndPopups()
        {
            var a = await Registered<ScreenA>();
            var b = await Registered<PopupA>();
            var c = await Registered<PopupB>();
            await _ui.Show<ScreenA>(new FakeViewModel());
            await _ui.Show<PopupA>(new FakeViewModel());
            await _ui.Show<PopupB>(new FakeViewModel());

            _ui.HideAll();

            Assert.AreEqual(1, a.UnbindCalls);
            Assert.AreEqual(1, b.UnbindCalls);
            Assert.AreEqual(1, c.UnbindCalls);
            Assert.IsNull(_ui.DebugActiveScreen);
            CollectionAssert.IsEmpty(_ui.DebugPopupStack);
        }

        [Test]
        public void HideAll_EmptyState_DoesNotFireVisibilityCallback()
        {
            var events = new List<bool>();
            _ui.SetVisibilityCallback(events.Add);

            _ui.HideAll();

            CollectionAssert.IsEmpty(events);
        }

        [Test]
        public async Task HideAllAsync_EmptyState_NoOp()
        {
            await _ui.HideAllAsync();
        }

        [Test]
        public async Task HideAllAsync_HidesEverything()
        {
            var a = await Registered<ScreenA>();
            var b = await Registered<PopupA>();
            await _ui.Show<ScreenA>(new FakeViewModel());
            await _ui.Show<PopupA>(new FakeViewModel());

            await _ui.HideAllAsync();

            Assert.AreEqual(ViewState.Hidden, a.State);
            Assert.AreEqual(ViewState.Hidden, b.State);
        }

        [Test]
        public async Task VisibilityCallback_FiresTrueOnFirstShowAndFalseWhenLastHides()
        {
            await Registered<ScreenA>();
            var events = new List<bool>();
            _ui.SetVisibilityCallback(events.Add);

            await _ui.Show<ScreenA>(new FakeViewModel());
            _ui.Hide<ScreenA>();

            CollectionAssert.AreEqual(new[] { true, false }, events);
        }

        // ── Unregister and Dispose ───────────────────────────────

        [Test]
        public async Task Unregister_ActiveScreen_HidesDetachesAndDisposesViewModel()
        {
            var a = await Registered<ScreenA>();
            var vm = new FakeViewModel();
            await _ui.Show<ScreenA>(vm);

            _ui.Unregister<ScreenA>();

            Assert.AreEqual(1, a.UnbindCalls);
            Assert.AreEqual(1, vm.DisposeCalls);
            Assert.IsNull(a.Root.parent);
            Assert.IsNull(_ui.DebugActiveScreen);
            Assert.Throws<InvalidOperationException>(() => _ui.Get<ScreenA>());
        }

        [Test]
        public async Task Unregister_ViewWithUxml_ReleasesHandle()
        {
            await _ui.Register<UxmlScreen>();

            _ui.Unregister<UxmlScreen>();

            CollectionAssert.AreEqual(new[] { nameof(UxmlScreen) }, _loader.Released);
        }

        [Test]
        public void Unregister_Unregistered_NoOp()
        {
            Assert.DoesNotThrow(() => _ui.Unregister<ScreenA>());
        }

        [Test]
        public async Task Dispose_DestroysAllViewsAndDisposesViewModels()
        {
            var a = await Registered<ScreenA>();
            await _ui.Register<UxmlPopup>();
            var uxmlView = _ui.Get<UxmlPopup>();
            var vm = new FakeViewModel();
            await _ui.Show<ScreenA>(vm);

            _ui.Dispose();

            Assert.IsNull(a.Root.parent);
            Assert.IsNull(uxmlView.Root.parent);
            Assert.AreEqual(1, vm.DisposeCalls);
            CollectionAssert.AreEqual(new[] { nameof(UxmlPopup) }, _loader.Released);
        }
    }
}
