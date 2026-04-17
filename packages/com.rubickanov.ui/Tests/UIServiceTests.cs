using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class UIServiceTests
    {
        private FakeViewFactory _factory = null!;
        private UIService _ui = null!;
        private FakeViewA _a = null!;
        private FakeViewB _b = null!;
        private FakeViewC _c = null!;

        [SetUp]
        public void SetUp()
        {
            _factory = new FakeViewFactory();
            _ui = new UIService(_factory);
            _a = new FakeViewA();
            _b = new FakeViewB();
            _c = new FakeViewC();
            _factory.Preset<FakeViewA>(_a);
            _factory.Preset<FakeViewB>(_b);
            _factory.Preset<FakeViewC>(_c);
        }

        [TearDown]
        public void TearDown() => _ui?.Dispose();

        [Test]
        public void Get_Unregistered_ThrowsInvalidOperation()
        {
            Assert.Throws<InvalidOperationException>(() => _ui.Get<FakeViewA>());
        }

        [Test]
        public async Task Register_ThenGet_ReturnsFactoryProducedView()
        {
            await _ui.Register<FakeViewA>(UILayer.Screen);

            Assert.AreSame(_a, _ui.Get<FakeViewA>());
        }

        [Test]
        public async Task ShowScreen_NoActive_BindsAndShows()
        {
            await _ui.Register<FakeViewA>(UILayer.Screen);
            var vm = new FakeViewModel();

            await _ui.Show<FakeViewA>(vm);

            Assert.AreEqual(1, _a.BindCalls);
            Assert.AreEqual(1, _a.ShowCalls);
            Assert.AreSame(vm, _a.LastViewModel);
            Assert.IsTrue(_a.IsVisible);
        }

        [Test]
        public async Task ShowScreen_ReplacesActiveScreen_HidesPreviousOne()
        {
            await _ui.Register<FakeViewA>(UILayer.Screen);
            await _ui.Register<FakeViewB>(UILayer.Screen);

            await _ui.Show<FakeViewA>(new FakeViewModel());
            await _ui.Show<FakeViewB>(new FakeViewModel());

            Assert.AreEqual(1, _a.HideCalls);
            Assert.IsFalse(_a.IsVisible);
            Assert.IsTrue(_b.IsVisible);
        }

        [Test]
        public async Task ShowPopup_NewView_AddsToStackAndShows()
        {
            await _ui.Register<FakeViewA>(UILayer.Popup);

            await _ui.Show<FakeViewA>(new FakeViewModel());

            Assert.AreEqual(1, _a.ShowCalls);
            Assert.IsTrue(_a.IsVisible);
        }

        [Test]
        public async Task ShowPopup_AlreadyShown_HidesPreviousInstanceAndRebinds()
        {
            await _ui.Register<FakeViewA>(UILayer.Popup);
            var firstVm = new FakeViewModel();
            var secondVm = new FakeViewModel();

            await _ui.Show<FakeViewA>(firstVm);
            await _ui.Show<FakeViewA>(secondVm);

            Assert.AreEqual(1, _a.HideCalls);
            Assert.AreEqual(2, _a.BindCalls);
            Assert.AreEqual(2, _a.ShowCalls);
            Assert.AreSame(secondVm, _a.LastViewModel);
        }

        [Test]
        public async Task ShowPopup_AlreadyShown_StackContainsViewOnce()
        {
            await _ui.Register<FakeViewA>(UILayer.Popup);
            await _ui.Register<FakeViewB>(UILayer.Popup);

            await _ui.Show<FakeViewA>(new FakeViewModel());
            await _ui.Show<FakeViewA>(new FakeViewModel());
            await _ui.Show<FakeViewB>(new FakeViewModel());

            _ui.HideTop();
            _ui.HideTop();
            _ui.HideTop();

            // A.Hide: 1 from re-bind cleanup + 1 from second HideTop = 2.
            // A phantom A in the stack would bump this to 3 on the third HideTop.
            Assert.AreEqual(2, _a.HideCalls);
            Assert.AreEqual(1, _b.HideCalls);
        }

        [Test]
        public async Task ShowScreen_BindThrows_RollsBackActiveScreen()
        {
            await _ui.Register<FakeViewA>(UILayer.Screen);
            _a.ThrowOnBind = new InvalidOperationException("boom");

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _ui.Show<FakeViewA>(new FakeViewModel()));

            Assert.AreEqual(1, _a.HideCalls);
            await _ui.Register<FakeViewB>(UILayer.Screen);
            await _ui.Show<FakeViewB>(new FakeViewModel());
            Assert.IsTrue(_b.IsVisible, "Active screen should have been cleared after failed Show.");
        }

        [Test]
        public async Task ShowPopup_ShowAsyncThrows_RemovesFromStack()
        {
            await _ui.Register<FakeViewA>(UILayer.Popup);
            _a.ThrowOnShowAsync = new InvalidOperationException("boom");

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _ui.Show<FakeViewA>(new FakeViewModel()));

            _ui.HideTop();
            Assert.AreEqual(1, _a.HideCalls,
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
            await _ui.Show<FakeViewA>(new FakeViewModel());

            _ui.Hide<FakeViewA>();

            Assert.IsFalse(_a.IsVisible);
            await _ui.Register<FakeViewB>(UILayer.Screen);
            await _ui.Show<FakeViewB>(new FakeViewModel());
            Assert.AreEqual(1, _a.HideCalls, "Hiding already-cleared screen must not hide it again.");
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
            await _ui.Show<FakeViewA>(new FakeViewModel());
            await _ui.Show<FakeViewB>(new FakeViewModel());

            _ui.HideTop();

            Assert.AreEqual(0, _a.HideCalls);
            Assert.AreEqual(1, _b.HideCalls);
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

            Assert.AreEqual(1, _a.HideCalls);
            Assert.AreEqual(1, _b.HideCalls);
            Assert.AreEqual(1, _c.HideCalls);
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

            Assert.AreEqual(1, _a.HideCalls);
            Assert.AreEqual(1, _b.HideCalls);
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
            await _ui.Show<FakeViewA>(new FakeViewModel());

            _ui.Unregister<FakeViewA>();

            Assert.AreEqual(1, _a.HideCalls);
            Assert.AreEqual(1, _a.DestroyCalls);
            CollectionAssert.Contains(_factory.Detached, _a);
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
            await _ui.Register<FakeViewB>(UILayer.Popup);

            _ui.Dispose();

            Assert.AreEqual(1, _a.DestroyCalls);
            Assert.AreEqual(1, _b.DestroyCalls);
        }
    }
}
