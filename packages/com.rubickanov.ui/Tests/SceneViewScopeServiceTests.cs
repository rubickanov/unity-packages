using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class SceneViewScopeServiceTests
    {
        private RecordingUxmlLoader _loader = null!;
        private UIService _ui = null!;

        [SetUp]
        public void SetUp()
        {
            _loader = new RecordingUxmlLoader();
            _ui = new UIService(TestRoot.Create(), _loader.Load);
        }

        [TearDown]
        public void TearDown() => _ui?.Dispose();

        [Test]
        public void HasActiveScope_Initially_False()
        {
            using var service = new SceneViewScopeService(_ui);

            Assert.IsFalse(service.HasActiveScope);
        }

        [Test]
        public void Begin_Returns_ActiveScope()
        {
            using var service = new SceneViewScopeService(_ui);

            var scope = service.Begin();

            Assert.IsNotNull(scope);
            Assert.IsTrue(service.HasActiveScope);
        }

        [Test]
        public async Task Begin_Twice_DisposesPreviousScope()
        {
            var service = new SceneViewScopeService(_ui);
            var first = service.Begin();
            await first.Register<ScreenA>();

            service.Begin();

            Assert.Throws<InvalidOperationException>(() => _ui.Get<ScreenA>(),
                "Previous scope should have been disposed, unregistering its views.");
        }

        [Test]
        public void Begin_DuringSlowLoadOfPreviousScope_LeavesNothingOfOldScopeRegistered()
        {
            _loader.Deferred = true;
            using var service = new SceneViewScopeService(_ui);
            var registration = service.Begin().Register<UxmlScreen>();

            service.Begin();
            _loader.Complete(nameof(UxmlScreen));

            Assert.CatchAsync<OperationCanceledException>(async () => await registration);
            Assert.Throws<InvalidOperationException>(() => _ui.Get<UxmlScreen>());
            CollectionAssert.AreEqual(new[] { nameof(UxmlScreen) }, _loader.Released);
        }

        [Test]
        public async Task Begin_NewScope_IndependentFromOldRegistrations()
        {
            using var service = new SceneViewScopeService(_ui);
            var first = service.Begin();
            await first.Register<ScreenA>();

            var second = service.Begin();
            await second.Register<PopupA>();

            Assert.Throws<InvalidOperationException>(() => _ui.Get<ScreenA>());
            Assert.DoesNotThrow(() => _ui.Get<PopupA>());
        }

        [Test]
        public async Task Dispose_DisposesActiveScope()
        {
            var service = new SceneViewScopeService(_ui);
            var scope = service.Begin();
            await scope.Register<ScreenA>();

            service.Dispose();

            Assert.Throws<InvalidOperationException>(() => _ui.Get<ScreenA>());
            Assert.IsFalse(service.HasActiveScope);
        }

        [Test]
        public void Dispose_WithoutActiveScope_IsSafe()
        {
            var service = new SceneViewScopeService(_ui);

            Assert.DoesNotThrow(() => service.Dispose());
        }
    }
}
