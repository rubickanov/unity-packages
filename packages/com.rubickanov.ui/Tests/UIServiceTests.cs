using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
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

        [Test]
        public void Ctor_RootWithoutLayers_ThrowsInvalidOperation()
        {
            Assert.Throws<InvalidOperationException>(() => new UIService(new VisualElement(), _loader.Load));
        }

        [Test]
        public void Get_Unregistered_ThrowsInvalidOperation()
        {
            Assert.Throws<InvalidOperationException>(() => _ui.Get<FakeViewA>());
        }

        [Test]
        public async Task Register_ThenGet_ReturnsViewAttachedToItsLayer()
        {
            await _ui.Register<FakeViewA>(UILayer.Popup);

            var view = _ui.Get<FakeViewA>();

            Assert.AreSame(_root.Q("popup-layer"), view.Root.parent);
            Assert.IsFalse(view.IsVisible);
            Assert.AreEqual(DisplayStyle.None, view.Root.style.display.value);
        }

        [Test]
        public async Task Register_CodeOnlyView_DoesNotLoadUxml()
        {
            await _ui.Register<FakeViewA>(UILayer.Screen);

            CollectionAssert.IsEmpty(_loader.Loaded);
        }

        [Test]
        public async Task Register_ViewWithUxml_LoadsByViewTypeName()
        {
            await _ui.Register<FakeUxmlView>(UILayer.Screen);

            CollectionAssert.AreEqual(new[] { nameof(FakeUxmlView) }, _loader.Loaded);
            CollectionAssert.IsEmpty(_loader.Released);
        }

        [Test]
        public async Task ShowScreen_NoActive_BindsAndShows()
        {
            await _ui.Register<FakeViewA>(UILayer.Screen);
            var a = _ui.Get<FakeViewA>();
            var vm = new FakeViewModel();

            await _ui.Show<FakeViewA>(vm);

            Assert.AreEqual(1, a.BindCalls);
            Assert.AreEqual(1, a.ShowCalls);
            Assert.AreSame(vm, a.LastViewModel);
            Assert.IsTrue(a.IsVisible);
        }

        [Test]
        public async Task ShowScreen_ReplacesActiveScreen_HidesPreviousOne()
        {
            await _ui.Register<FakeViewA>(UILayer.Screen);
            await _ui.Register<FakeViewB>(UILayer.Screen);
            var a = _ui.Get<FakeViewA>();
            var b = _ui.Get<FakeViewB>();

            await _ui.Show<FakeViewA>(new FakeViewModel());
            await _ui.Show<FakeViewB>(new FakeViewModel());

            Assert.AreEqual(1, a.HideCalls);
            Assert.IsFalse(a.IsVisible);
            Assert.IsTrue(b.IsVisible);
        }

        [Test]
        public async Task ShowPopup_NewView_AddsToStackAndShows()
        {
            await _ui.Register<FakeViewA>(UILayer.Popup);
            var a = _ui.Get<FakeViewA>();

            await _ui.Show<FakeViewA>(new FakeViewModel());

            Assert.AreEqual(1, a.ShowCalls);
            Assert.IsTrue(a.IsVisible);
            CollectionAssert.AreEqual(new[] { a }, _ui.DebugPopupStack);
        }

        [Test]
        public async Task ShowPopup_AlreadyShown_HidesPreviousInstanceAndRebinds()
        {
            await _ui.Register<FakeViewA>(UILayer.Popup);
            var a = _ui.Get<FakeViewA>();
            var firstVm = new FakeViewModel();
            var secondVm = new FakeViewModel();

            await _ui.Show<FakeViewA>(firstVm);
            await _ui.Show<FakeViewA>(secondVm);

            Assert.AreEqual(1, a.HideCalls);
            Assert.AreEqual(2, a.BindCalls);
            Assert.AreEqual(2, a.ShowCalls);
            Assert.AreSame(secondVm, a.LastViewModel);
        }

        [Test]
        public async Task ShowPopup_AlreadyShown_StackContainsViewOnce()
        {
            await _ui.Register<FakeViewA>(UILayer.Popup);
            await _ui.Register<FakeViewB>(UILayer.Popup);
            var a = _ui.Get<FakeViewA>();
            var b = _ui.Get<FakeViewB>();

            await _ui.Show<FakeViewA>(new FakeViewModel());
            await _ui.Show<FakeViewA>(new FakeViewModel());
            await _ui.Show<FakeViewB>(new FakeViewModel());

            CollectionAssert.AreEqual(new FakeView[] { a, b }, _ui.DebugPopupStack);
            _ui.HideTop();
            _ui.HideTop();
            _ui.HideTop();

            CollectionAssert.IsEmpty(_ui.DebugPopupStack);
            Assert.AreEqual(2, a.HideCalls);
            Assert.AreEqual(1, b.HideCalls);
        }

        [Test]
        public async Task ShowScreen_BindThrows_RollsBackActiveScreen()
        {
            await _ui.Register<FakeViewA>(UILayer.Screen);
            var a = _ui.Get<FakeViewA>();
            a.ThrowOnBind = new InvalidOperationException("boom");

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _ui.Show<FakeViewA>(new FakeViewModel()));

            Assert.IsFalse(a.IsVisible);
            Assert.IsNull(_ui.DebugActiveScreen);
            await _ui.Register<FakeViewB>(UILayer.Screen);
            await _ui.Show<FakeViewB>(new FakeViewModel());
            Assert.IsTrue(_ui.Get<FakeViewB>().IsVisible, "Active screen should have been cleared after failed Show.");
        }

        [Test]
        public async Task ShowPopup_ShowAsyncThrows_RemovesFromStack()
        {
            await _ui.Register<FakeViewA>(UILayer.Popup);
            var a = _ui.Get<FakeViewA>();
            a.ThrowOnShowAsync = new InvalidOperationException("boom");

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _ui.Show<FakeViewA>(new FakeViewModel()));

            CollectionAssert.IsEmpty(_ui.DebugPopupStack);
            _ui.HideTop();
            Assert.AreEqual(1, a.HideCalls,
                "HideTop after failed popup show must not hide FakeViewA again.");
        }

        [Test]
        public async Task Hide_Unregistered_NoOp()
        {
            Assert.DoesNotThrow(() => _ui.Hide<FakeViewA>());
            await UniTask.CompletedTask;
        }

        [Test]
        public async Task Hide_ActiveScreen_ClearsActive()
        {
            await _ui.Register<FakeViewA>(UILayer.Screen);
            var a = _ui.Get<FakeViewA>();
            await _ui.Show<FakeViewA>(new FakeViewModel());

            _ui.Hide<FakeViewA>();

            Assert.IsFalse(a.IsVisible);
            Assert.IsNull(_ui.DebugActiveScreen);
            await _ui.Register<FakeViewB>(UILayer.Screen);
            await _ui.Show<FakeViewB>(new FakeViewModel());
            Assert.AreEqual(1, a.HideCalls, "Hiding already-cleared screen must not hide it again.");
        }

        [Test]
        public async Task HideTop_EmptyStack_NoOp()
        {
            await _ui.Register<FakeViewA>(UILayer.Popup);

            Assert.DoesNotThrow(() => _ui.HideTop());
        }

        [Test]
        public async Task HideTop_RemovesTopmostPopup()
        {
            await _ui.Register<FakeViewA>(UILayer.Popup);
            await _ui.Register<FakeViewB>(UILayer.Popup);
            var a = _ui.Get<FakeViewA>();
            var b = _ui.Get<FakeViewB>();
            await _ui.Show<FakeViewA>(new FakeViewModel());
            await _ui.Show<FakeViewB>(new FakeViewModel());

            _ui.HideTop();

            Assert.AreEqual(0, a.HideCalls);
            Assert.AreEqual(1, b.HideCalls);
            CollectionAssert.AreEqual(new[] { a }, _ui.DebugPopupStack);
        }

        [Test]
        public async Task HideAll_ClearsScreenAndPopups()
        {
            await _ui.Register<FakeViewA>(UILayer.Screen);
            await _ui.Register<FakeViewB>(UILayer.Popup);
            await _ui.Register<FakeViewC>(UILayer.Popup);
            await _ui.Show<FakeViewA>(new FakeViewModel());
            await _ui.Show<FakeViewB>(new FakeViewModel());
            await _ui.Show<FakeViewC>(new FakeViewModel());

            _ui.HideAll();

            Assert.AreEqual(1, _ui.Get<FakeViewA>().HideCalls);
            Assert.AreEqual(1, _ui.Get<FakeViewB>().HideCalls);
            Assert.AreEqual(1, _ui.Get<FakeViewC>().HideCalls);
        }

        [Test]
        public void HideAll_EmptyState_DoesNotFireVisibilityCallback()
        {
            var events = new System.Collections.Generic.List<bool>();
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
            await _ui.Register<FakeViewA>(UILayer.Screen);
            await _ui.Register<FakeViewB>(UILayer.Popup);
            await _ui.Show<FakeViewA>(new FakeViewModel());
            await _ui.Show<FakeViewB>(new FakeViewModel());

            await _ui.HideAllAsync();

            Assert.AreEqual(1, _ui.Get<FakeViewA>().HideCalls);
            Assert.AreEqual(1, _ui.Get<FakeViewB>().HideCalls);
        }

        [Test]
        public async Task VisibilityCallback_FiresTrueOnFirstShowAndFalseWhenLastHides()
        {
            await _ui.Register<FakeViewA>(UILayer.Screen);
            var events = new System.Collections.Generic.List<bool>();
            _ui.SetVisibilityCallback(events.Add);

            await _ui.Show<FakeViewA>(new FakeViewModel());
            _ui.Hide<FakeViewA>();

            CollectionAssert.AreEqual(new[] { true, false }, events);
        }

        [Test]
        public async Task Unregister_ActiveScreen_HidesAndDetaches()
        {
            await _ui.Register<FakeViewA>(UILayer.Screen);
            var a = _ui.Get<FakeViewA>();
            await _ui.Show<FakeViewA>(new FakeViewModel());

            _ui.Unregister<FakeViewA>();

            Assert.AreEqual(1, a.HideCalls);
            Assert.IsNull(a.Root.parent);
            Assert.Throws<InvalidOperationException>(() => _ui.Get<FakeViewA>());
        }

        [Test]
        public async Task Unregister_ViewWithUxml_ReleasesHandle()
        {
            await _ui.Register<FakeUxmlView>(UILayer.Screen);

            _ui.Unregister<FakeUxmlView>();

            CollectionAssert.AreEqual(new[] { nameof(FakeUxmlView) }, _loader.Released);
        }

        [Test]
        public void Unregister_Unregistered_NoOp()
        {
            Assert.DoesNotThrow(() => _ui.Unregister<FakeViewA>());
        }

        [Test]
        public async Task Dispose_DestroysAllViews()
        {
            await _ui.Register<FakeViewA>(UILayer.Screen);
            await _ui.Register<FakeUxmlView>(UILayer.Popup);
            var a = _ui.Get<FakeViewA>();
            var uxmlView = _ui.Get<FakeUxmlView>();

            _ui.Dispose();

            Assert.IsNull(a.Root.parent);
            Assert.IsNull(uxmlView.Root.parent);
            CollectionAssert.AreEqual(new[] { nameof(FakeUxmlView) }, _loader.Released);
        }
    }
}
