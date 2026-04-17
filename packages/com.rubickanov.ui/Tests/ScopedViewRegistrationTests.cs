using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class ScopedViewRegistrationTests
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
            _factory.Preset<FakeViewC>(new FakeViewC());
        }

        [TearDown]
        public void TearDown() => _ui?.Dispose();

        [Test]
        public async Task Register_AddsViewToUI()
        {
            var scope = new ScopedViewRegistration(_ui);

            await scope.Register<FakeViewA>(UILayer.Screen);

            Assert.DoesNotThrow(() => _ui.Get<FakeViewA>());
        }

        [Test]
        public async Task Dispose_UnregistersAllViews()
        {
            var scope = new ScopedViewRegistration(_ui);
            await scope.Register<FakeViewA>(UILayer.Screen);
            await scope.Register<FakeViewB>(UILayer.Popup);

            scope.Dispose();

            Assert.Throws<InvalidOperationException>(() => _ui.Get<FakeViewA>());
            Assert.Throws<InvalidOperationException>(() => _ui.Get<FakeViewB>());
        }

        [Test]
        public void Dispose_ExecutesActionsInLifoOrder()
        {
            var order = new List<string>();
            var scope = new ScopedViewRegistration(_ui);

            // Use reflection-free injection: register via a fake IUIService that records order.
            var recorder = new RecordingUIService(order);
            var recordedScope = new ScopedViewRegistration(recorder);

            // Simulate three registrations. Register only adds the cleanup; Unregister records order.
            recordedScope.Register<FakeViewA>(UILayer.Screen).GetAwaiter().GetResult();
            recordedScope.Register<FakeViewB>(UILayer.Popup).GetAwaiter().GetResult();
            recordedScope.Register<FakeViewC>(UILayer.HUD).GetAwaiter().GetResult();

            recordedScope.Dispose();

            Assert.AreEqual(new[] { "C", "B", "A" }, order);
        }

        [Test]
        public void Dispose_CleanupAction_Throws_AllOtherCleanupsStillRun()
        {
            var order = new List<string>();
            var recorder = new RecordingUIService(order) { FailOnUnregisterType = typeof(FakeViewB) };
            var scope = new ScopedViewRegistration(recorder);

            scope.Register<FakeViewA>(UILayer.Screen).GetAwaiter().GetResult();
            scope.Register<FakeViewB>(UILayer.Popup).GetAwaiter().GetResult();
            scope.Register<FakeViewC>(UILayer.HUD).GetAwaiter().GetResult();

            Assert.Throws<AggregateException>(() => scope.Dispose());
            // C runs, B throws but doesn't stop iteration, A still runs.
            Assert.AreEqual(new[] { "C", "A" }, order);
        }

        [Test]
        public void Dispose_CalledTwice_IsSafe()
        {
            var scope = new ScopedViewRegistration(_ui);

            scope.Dispose();
            Assert.DoesNotThrow(() => scope.Dispose());
        }

        private sealed class RecordingUIService : IUIService
        {
            private readonly List<string> _order;
            public Type? FailOnUnregisterType;

            public RecordingUIService(List<string> order) => _order = order;

            public Cysharp.Threading.Tasks.UniTask Register<T>(UILayer layer) where T : class, IView
                => Cysharp.Threading.Tasks.UniTask.CompletedTask;

            public void Unregister<T>() where T : IView
            {
                if (FailOnUnregisterType == typeof(T))
                    throw new InvalidOperationException($"Fail on {typeof(T).Name}");
                _order.Add(typeof(T).Name.Replace("FakeView", string.Empty));
            }

            public T Get<T>() where T : IView => throw new NotSupportedException();
            public Cysharp.Threading.Tasks.UniTask Show<T>(ViewModelBase viewModel) where T : IView
                => Cysharp.Threading.Tasks.UniTask.CompletedTask;
            public void Hide<T>() where T : IView { }
            public Cysharp.Threading.Tasks.UniTask HideAsync<T>(float duration = 0.3f) where T : IView
                => Cysharp.Threading.Tasks.UniTask.CompletedTask;
            public void HideTop() { }
            public Cysharp.Threading.Tasks.UniTask HideTopAsync(float duration = 0.3f)
                => Cysharp.Threading.Tasks.UniTask.CompletedTask;
            public void HideAll() { }
            public Cysharp.Threading.Tasks.UniTask HideAllAsync(float duration = 0.3f)
                => Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }
    }
}
