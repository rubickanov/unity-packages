using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class InputTests
    {
        private UIService _ui = null!;
        private readonly List<bool> _written = new();

        [SetUp]
        public void SetUp()
        {
            _ui = new UIService(TestRoot.Create(), new RecordingUxmlLoader().Load);
            _written.Clear();
        }

        [TearDown]
        public void TearDown() => _ui.Dispose();

        // ── CursorLock ───────────────────────────────────────────

        [Test]
        public void CursorLock_NothingLooks_FreeFromTheStart()
        {
            using var cursor = new CursorLock(_ui, _written.Add);

            Assert.IsFalse(cursor.IsLocked);
            CollectionAssert.AreEqual(new[] { false }, _written);
        }

        [Test]
        public void CursorLock_LookedWithAndNoCapture_LockedUntilHandleGoes()
        {
            using var cursor = new CursorLock(_ui, _written.Add);

            var look = cursor.Lock();
            var lockedWhileLooking = cursor.IsLocked;
            look.Dispose();
            look.Dispose();

            Assert.IsTrue(lockedWhileLooking);
            Assert.IsFalse(cursor.IsLocked);
            CollectionAssert.AreEqual(new[] { false, true, false }, _written);
        }

        [Test]
        public async Task CursorLock_ScreenShownWhileLooking_FreeUntilItHides()
        {
            await _ui.Register<ScreenA>();
            using var cursor = new CursorLock(_ui, _written.Add);
            using var look = cursor.Lock();

            await _ui.Show<ScreenA, FakeViewModel>(new FakeViewModel());
            var lockedUnderScreen = cursor.IsLocked;
            _ui.Hide<ScreenA>();

            Assert.IsFalse(lockedUnderScreen);
            Assert.IsTrue(cursor.IsLocked);
        }

        [Test]
        public void CursorLock_CaptureTakenByTheGame_Frees()
        {
            using var cursor = new CursorLock(_ui, _written.Add);
            using var look = cursor.Lock();

            using (_ui.CapturePointer())
                Assert.IsFalse(cursor.IsLocked);

            Assert.IsTrue(cursor.IsLocked);
        }

        [Test]
        public void CursorLock_Disposed_FreesAndStopsFollowing()
        {
            var cursor = new CursorLock(_ui, _written.Add);
            cursor.Lock();

            cursor.Dispose();
            _written.Clear();
            using (_ui.CapturePointer()) { }

            Assert.IsFalse(cursor.IsLocked);
            CollectionAssert.IsEmpty(_written);
        }

        // ── BackOrPause ──────────────────────────────────────────

        [Test]
        public async Task BackOrPause_EscapeWithScreenToGoBackFrom_GoesBackAndDoesNotPause()
        {
            await _ui.Register<ScreenA>();
            await _ui.Register<ScreenB>();
            await _ui.Navigate<ScreenA, FakeViewModel>(() => new FakeViewModel());
            await _ui.Navigate<ScreenB, FakeViewModel>(() => new FakeViewModel());
            var paused = 0;

            var taken = _ui.BackOrPause(back: true, pause: true, () => paused++);

            Assert.IsTrue(taken);
            Assert.AreEqual(0, paused);
            Assert.AreEqual(ViewState.Shown, _ui.Get<ScreenA>().State);
        }

        [Test]
        public void BackOrPause_EscapeWithNothingOpen_Pauses()
        {
            var paused = 0;

            var taken = _ui.BackOrPause(back: true, pause: true, () => paused++);

            Assert.IsTrue(taken);
            Assert.AreEqual(1, paused);
        }

        [Test]
        public void BackOrPause_BackOnlyWithNothingOpen_DoesNothing()
        {
            var paused = 0;

            var taken = _ui.BackOrPause(back: true, pause: false, () => paused++);

            Assert.IsFalse(taken);
            Assert.AreEqual(0, paused);
        }

        [Test]
        public void BackOrPause_NoPress_NothingAsked()
        {
            var asked = 0;
            using var handler = _ui.PushBackHandler(() => ++asked > 0);

            var taken = _ui.BackOrPause(back: false, pause: false, () => asked++);

            Assert.IsFalse(taken);
            Assert.AreEqual(0, asked);
        }

        // ── OpenInputScope ───────────────────────────────────────

        [Test]
        public void OpenInputScope_WithMenu_TakesBackPointerAndKeysUntilDisposed()
        {
            using var navigation = new MenuNavigation();
            var menu = new MenuModel(new[] { new MenuEntry("RESUME", null) }, navigation, claim: false);
            var backs = 0;

            var scope = _ui.OpenInputScope(() => ++backs > 0, navigation, menu);
            var state = (_ui.PointerCaptured.CurrentValue, navigation.Current, _ui.Back());
            scope.Dispose();
            scope.Dispose();

            Assert.AreEqual((true, (IMenuTarget)menu, true), state);
            Assert.AreEqual(1, backs);
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
            Assert.IsNull(navigation.Current);
            Assert.IsFalse(_ui.Back());
            menu.Dispose();
        }

        [Test]
        public void OpenInputScope_NoBackHandler_BackStopsThere()
        {
            var under = 0;
            using var handler = _ui.PushBackHandler(() => ++under > 0);

            using var scope = _ui.OpenInputScope();

            Assert.IsTrue(_ui.Back());
            Assert.AreEqual(0, under);
        }

        [Test]
        public void OpenInputScope_MenuWithoutNavigation_Throws()
        {
            var menu = new MenuModel(new[] { new MenuEntry("RESUME", null) }, null);

            Assert.Throws<System.ArgumentNullException>(() => _ui.OpenInputScope(null, null, menu));
            Assert.IsFalse(_ui.PointerCaptured.CurrentValue);
        }
    }
}
