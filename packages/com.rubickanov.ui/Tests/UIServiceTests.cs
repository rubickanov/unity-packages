using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using R3;
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
        public async Task Register_PopupView_NothingAttachedAndGetThrows()
        {
            await _ui.Register<PopupA>();

            var ex = Assert.Throws<InvalidOperationException>(() => _ui.Get<PopupA>());

            StringAssert.Contains("ShowView", ex.Message);
            Assert.AreEqual(0, _root.Q("popup-layer").childCount);
            CollectionAssert.AreEqual(new[] { typeof(PopupA) }, _ui.DebugPopupViews);
        }

        [Test]
        public async Task Register_PopupViewTwice_Throws()
        {
            await _ui.Register<PopupA>();

            Assert.ThrowsAsync<InvalidOperationException>(async () => await _ui.Register<PopupA>());
        }

        [Test]
        public async Task Show_PopupView_Throws()
        {
            await _ui.Register<PopupA>();

            Assert.ThrowsAsync<InvalidOperationException>(async () => await _ui.Show<PopupA>(new FakeViewModel()));
        }

        [Test]
        public async Task Unregister_PopupViewWithUxml_ReleasesHandle()
        {
            await _ui.Register<UxmlPopup>();

            _ui.Unregister<UxmlPopup>();

            CollectionAssert.AreEqual(new[] { nameof(UxmlPopup) }, _loader.Released);
            CollectionAssert.IsEmpty(_ui.DebugPopupViews);
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
            var a = await Registered<HudA>();
            var b = await Registered<HudB>();
            var vm = new FakeViewModel();
            await _ui.Show<HudA>(vm);

            var ex = Assert.ThrowsAsync<ArgumentException>(async () => await _ui.Show<HudA>(new OtherViewModel()));

            StringAssert.Contains(nameof(HudA), ex.Message);
            StringAssert.Contains(nameof(FakeViewModel), ex.Message);
            StringAssert.Contains(nameof(OtherViewModel), ex.Message);
            Assert.ThrowsAsync<ArgumentException>(async () => await _ui.Show<HudB>(new OtherViewModel()));
            Assert.AreEqual(ViewState.Shown, a.State);
            Assert.AreEqual(1, a.BindCalls);
            Assert.AreEqual(0, vm.DisposeCalls);
            Assert.AreEqual(ViewState.Hidden, b.State);
            Assert.AreEqual(0, b.BindCalls);
        }

        [Test]
        public async Task Show_SameViewModelTwice_BindsOnceAndDoesNotDispose()
        {
            var a = await Registered<HudA>();
            var vm = new FakeViewModel();

            await _ui.Show<HudA>(vm);
            await _ui.Show<HudA>(vm);

            Assert.AreEqual(1, a.BindCalls);
            Assert.AreEqual(0, a.UnbindCalls);
            Assert.AreEqual(0, vm.DisposeCalls);
            Assert.AreEqual(ViewState.Shown, a.State);
        }

        [Test]
        public async Task Show_AlreadyShownWithOtherViewModel_RebindsAndDisposesOld()
        {
            var a = await Registered<HudA>();
            var firstVm = new FakeViewModel();
            var secondVm = new FakeViewModel();

            await _ui.Show<HudA>(firstVm);
            await _ui.Show<HudA>(secondVm);

            Assert.AreEqual(2, a.BindCalls);
            Assert.AreEqual(1, a.UnbindCalls);
            Assert.AreSame(secondVm, a.LastViewModel);
            Assert.AreEqual(1, firstVm.DisposeCalls);
            Assert.AreEqual(0, secondVm.DisposeCalls);
            Assert.AreEqual(ViewState.Shown, a.State);
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
        public async Task ShowScreen_AnimationThrows_HiddenAndNotActive()
        {
            var animation = new ControlledAnimation();
            var a = await Registered<ScreenA>(animation);
            var vm = new FakeViewModel();
            var show = _ui.Show<ScreenA>(vm);

            animation.FailShow(new InvalidOperationException("boom"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await show);
            Assert.IsNull(_ui.DebugActiveScreen);
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
            Assert.AreEqual(ViewState.Hidden, a.State);
            Assert.AreEqual(1, vm.DisposeCalls);
        }

        // ── Transitions (F1, F2, D4) ─────────────────────────────

        [Test]
        public async Task Show_DuringHide_ShownWithSecondViewModelAndFirstDisposedOnce()
        {
            var animation = new ControlledAnimation();
            var a = await Registered<HudA>(animation);
            var firstVm = new FakeViewModel();
            var secondVm = new FakeViewModel();
            var firstShow = _ui.Show<HudA>(firstVm);
            animation.CompleteShow();
            await firstShow;

            var hide = _ui.HideAsync<HudA>();
            var secondShow = _ui.Show<HudA>(secondVm);

            Assert.AreEqual(ViewState.Showing, a.State);
            Assert.AreSame(secondVm, a.LastViewModel);
            Assert.AreEqual(1, firstVm.DisposeCalls);

            animation.Hides[0].TrySetResult();
            animation.CompleteShow();
            await hide;
            await secondShow;

            Assert.AreEqual(ViewState.Shown, a.State);
            Assert.AreEqual(DisplayStyle.Flex, a.Root.style.display.value);
            Assert.AreEqual(1, firstVm.DisposeCalls);
            Assert.AreEqual(0, secondVm.DisposeCalls);
        }

        [Test]
        public async Task Show_DuringHideWithSameViewModel_KeepsBindingAndShows()
        {
            var animation = new ControlledAnimation();
            var a = await Registered<HudA>(animation);
            var vm = new FakeViewModel();
            var firstShow = _ui.Show<HudA>(vm);
            animation.CompleteShow();
            await firstShow;

            var hide = _ui.HideAsync<HudA>();
            var secondShow = _ui.Show<HudA>(vm);
            animation.CompleteShow();
            await hide;
            await secondShow;

            Assert.AreEqual(ViewState.Shown, a.State);
            Assert.AreEqual(1, a.BindCalls);
            Assert.AreEqual(0, vm.DisposeCalls);
        }

        [Test]
        public async Task HideAsync_DuringShow_HidingAtOnceThenHiddenAndDisposed()
        {
            var animation = new ControlledAnimation();
            var a = await Registered<HudA>(animation);
            var vm = new FakeViewModel();
            var show = _ui.Show<HudA>(vm);

            var hide = _ui.HideAsync<HudA>();

            Assert.AreEqual(ViewState.Hiding, a.State);
            Assert.IsFalse(a.IsVisible);
            Assert.AreEqual(PickingMode.Ignore, a.Root.pickingMode);
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
            var a = await Registered<HudA>(animation);
            var vm = new FakeViewModel();
            var show = _ui.Show<HudA>(vm);

            _ui.Hide<HudA>();
            await show;

            Assert.AreEqual(ViewState.Hidden, a.State);
            Assert.AreEqual(1, vm.DisposeCalls);
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
        public async Task ShowHud_NotActiveScreenAndDoesNotInterceptInput()
        {
            var hud = await Registered<HudA>();

            await _ui.Show<HudA>(new FakeViewModel());

            Assert.AreEqual(ViewState.Shown, hud.State);
            Assert.IsNull(_ui.DebugActiveScreen);
            Assert.AreEqual(PickingMode.Ignore, hud.Root.pickingMode);
        }

        [Test]
        public async Task ShowScreen_InterceptsInputByDefault()
        {
            var a = await Registered<ScreenA>();

            await _ui.Show<ScreenA>(new FakeViewModel());

            Assert.AreEqual(PickingMode.Position, a.Root.pickingMode);
        }

        [Test]
        public async Task HideScreen_HudShown_HidesScreenAndLeavesHud()
        {
            var hud = await Registered<HudA>();
            var screen = await Registered<ScreenA>();
            await _ui.Show<HudA>(new FakeViewModel());
            await _ui.Show<ScreenA>(new FakeViewModel());

            _ui.HideScreen();

            Assert.AreEqual(ViewState.Shown, hud.State);
            Assert.AreEqual(ViewState.Hidden, screen.State);
            Assert.IsNull(_ui.DebugActiveScreen);
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public async Task HideScreenAsync_ClearsAtOnceAndHidesAfterAnimation()
        {
            var animation = new ControlledAnimation();
            var a = await Registered<ScreenA>(animation);
            var show = _ui.Show<ScreenA>(new FakeViewModel());
            animation.CompleteShow();
            await show;

            var hide = _ui.HideScreenAsync();

            Assert.IsNull(_ui.DebugActiveScreen);
            Assert.AreEqual(ViewState.Hiding, a.State);
            animation.CompleteHide();
            await hide;
            Assert.AreEqual(ViewState.Hidden, a.State);
        }

        [Test]
        public async Task HideScreenAsync_NoScreen_NoOp()
        {
            await _ui.HideScreenAsync();

            Assert.IsNull(_ui.DebugActiveScreen);
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

        // ── Pointer capture (D8, F7) ─────────────────────────────

        [Test]
        public async Task ShowScreen_PointerCapturedUntilHide()
        {
            await Registered<ScreenA>();
            var events = new List<bool>();
            using var subscription = _ui.PointerCaptured.Subscribe(events.Add);

            await _ui.Show<ScreenA>(new FakeViewModel());
            var capturedWhileShown = _ui.PointerCaptured.CurrentValue;
            _ui.Hide<ScreenA>();

            Assert.IsTrue(capturedWhileShown);
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
            CollectionAssert.AreEqual(new[] { false, true, false }, events);
        }

        [Test]
        public async Task CapturePointer_ViewAndHandle_ReleasedOnlyWhenBothRelease()
        {
            await Registered<ScreenA>();
            await _ui.Show<ScreenA>(new FakeViewModel());
            var handle = _ui.CapturePointer();

            _ui.Hide<ScreenA>();
            var capturedAfterHide = _ui.PointerCaptured.CurrentValue;
            handle.Dispose();
            handle.Dispose();

            Assert.IsTrue(capturedAfterHide);
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public async Task ShowHud_DoesNotCapturePointer()
        {
            await Registered<HudA>();

            await _ui.Show<HudA>(new FakeViewModel());

            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
        }

        [Test]
        public async Task HideAsync_ReleasesCaptureWhenHideStarts()
        {
            var animation = new ControlledAnimation();
            await Registered<ScreenA>(animation);
            var show = _ui.Show<ScreenA>(new FakeViewModel());
            animation.CompleteShow();
            await show;

            var hide = _ui.HideAsync<ScreenA>();
            var capturedDuringHide = _ui.PointerCaptured.CurrentValue;
            animation.CompleteHide();
            await hide;

            Assert.IsFalse(capturedDuringHide);
            Assert.AreEqual(ViewState.Hidden, _ui.Get<ScreenA>().State);
        }

        [Test]
        public async Task ShowScreen_ReplacingScreen_CaptureStaysHeld()
        {
            await Registered<ScreenA>();
            await Registered<ScreenB>();
            await _ui.Show<ScreenA>(new FakeViewModel());

            await _ui.Show<ScreenB>(new FakeViewModel());
            var capturedWithB = _ui.PointerCaptured.CurrentValue;
            _ui.Hide<ScreenB>();

            Assert.IsTrue(capturedWithB);
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
        }

        // ── Back stack (D9, F8) ──────────────────────────────────

        [Test]
        public void Back_NoHandlers_ReturnsFalse()
        {
            Assert.IsFalse(_ui.Back());
        }

        [Test]
        public async Task Back_ScreenOnly_ReturnsFalseAndScreenStays()
        {
            var screen = await Registered<ScreenA>();
            await _ui.Show<ScreenA>(new FakeViewModel());

            var consumed = _ui.Back();

            Assert.IsFalse(consumed);
            Assert.AreEqual(ViewState.Shown, screen.State);
        }

        [Test]
        public void Back_HandlerReturnsFalse_TriesNext()
        {
            var calls = new List<string>();
            using var lower = _ui.PushBackHandler(() => { calls.Add("lower"); return true; });
            using var upper = _ui.PushBackHandler(() => { calls.Add("upper"); return false; });

            var consumed = _ui.Back();

            Assert.IsTrue(consumed);
            CollectionAssert.AreEqual(new[] { "upper", "lower" }, calls);
        }

        [Test]
        public void PushBackHandler_Disposed_NotCalled()
        {
            var called = false;
            var handle = _ui.PushBackHandler(() => called = true);

            handle.Dispose();
            var consumed = _ui.Back();

            Assert.IsFalse(consumed);
            Assert.IsFalse(called);
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
            var vm = new FakeViewModel();
            await _ui.Show<ScreenA>(vm);

            _ui.Dispose();

            Assert.IsNull(a.Root.parent);
            Assert.AreEqual(1, vm.DisposeCalls);
            CollectionAssert.AreEqual(new[] { nameof(UxmlPopup) }, _loader.Released);
        }

        [Test]
        public void Register_AfterDispose_ThrowsObjectDisposed()
        {
            _ui.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ui.Register<ScreenA>().GetAwaiter().GetResult());
        }

        [Test]
        public async Task Hide_OnUnbindThrows_ViewEndsUnboundAndViewModelDisposed()
        {
            var view = await Registered<ScreenA>();
            var vm = new FakeViewModel();
            await _ui.Show<ScreenA>(vm);
            view.ThrowOnUnbind = new InvalidOperationException("boom");

            Assert.Throws<InvalidOperationException>(() => _ui.Hide<ScreenA>());
            view.ThrowOnUnbind = null;
            await _ui.Show<ScreenA>(new FakeViewModel());

            Assert.AreEqual(1, vm.DisposeCalls);
            Assert.AreEqual(1, view.UnbindCalls);
            Assert.AreEqual(ViewState.Shown, view.State);
        }
    }
}
