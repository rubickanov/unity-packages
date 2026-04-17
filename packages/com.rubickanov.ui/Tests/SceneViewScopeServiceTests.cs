using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class SceneViewScopeServiceTests
    {
        private FakeViewFactory _factory = null!;
        private UIService _ui = null!;

        [SetUp]
        public void SetUp()
        {
            _factory = new FakeViewFactory();
            _ui = new UIService(_factory);
            _factory.Preset<FakeViewA>(new FakeViewA());
            _factory.Preset<FakeViewB>(new FakeViewB());
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
            await first.Register<FakeViewA>(UILayer.Screen);

            service.Begin();

            Assert.Throws<InvalidOperationException>(() => _ui.Get<FakeViewA>(),
                "Previous scope should have been disposed, unregistering its views.");
        }

        [Test]
        public async Task Begin_NewScope_IndependentFromOldRegistrations()
        {
            using var service = new SceneViewScopeService(_ui);
            var first = service.Begin();
            await first.Register<FakeViewA>(UILayer.Screen);

            var second = service.Begin();
            await second.Register<FakeViewB>(UILayer.Popup);

            Assert.Throws<InvalidOperationException>(() => _ui.Get<FakeViewA>());
            Assert.DoesNotThrow(() => _ui.Get<FakeViewB>());
        }

        [Test]
        public async Task Dispose_DisposesActiveScope()
        {
            var service = new SceneViewScopeService(_ui);
            var scope = service.Begin();
            await scope.Register<FakeViewA>(UILayer.Screen);

            service.Dispose();

            Assert.Throws<InvalidOperationException>(() => _ui.Get<FakeViewA>());
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
