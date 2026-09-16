using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class ScopedViewRegistrationTests
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
        public async Task Register_AddsViewToUI()
        {
            var scope = new ScopedViewRegistration(_ui);

            await scope.Register<ScreenA>();

            Assert.DoesNotThrow(() => _ui.Get<ScreenA>());
        }

        [Test]
        public async Task Dispose_UnregistersAllViews()
        {
            var scope = new ScopedViewRegistration(_ui);
            await scope.Register<ScreenA>();
            await scope.Register<PopupA>();

            scope.Dispose();

            Assert.Throws<InvalidOperationException>(() => _ui.Get<ScreenA>());
            Assert.Throws<InvalidOperationException>(() => _ui.Get<PopupA>());
        }

        [Test]
        public void Dispose_DuringSlowLoad_LeavesNothingRegistered()
        {
            _loader.Deferred = true;
            var scope = new ScopedViewRegistration(_ui);
            var registration = scope.Register<UxmlScreen>();

            scope.Dispose();
            _loader.Complete(nameof(UxmlScreen));

            Assert.CatchAsync<OperationCanceledException>(async () => await registration);
            Assert.Throws<InvalidOperationException>(() => _ui.Get<UxmlScreen>());
            CollectionAssert.AreEqual(new[] { nameof(UxmlScreen) }, _loader.Released);
        }

        [Test]
        public void Register_DisposedScope_ThrowsObjectDisposed()
        {
            var scope = new ScopedViewRegistration(_ui);
            scope.Dispose();

            Assert.ThrowsAsync<ObjectDisposedException>(async () => await scope.Register<ScreenA>());
            Assert.Throws<InvalidOperationException>(() => _ui.Get<ScreenA>());
        }

        [Test]
        public async Task Register_TypeRegisteredElsewhere_ThrowsAndDisposeKeepsOtherRegistration()
        {
            await _ui.Register<ScreenA>();
            var scope = new ScopedViewRegistration(_ui);

            Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.Register<ScreenA>());
            scope.Dispose();

            Assert.DoesNotThrow(() => _ui.Get<ScreenA>());
        }

        [Test]
        public void Dispose_ExecutesActionsInLifoOrder()
        {
            var order = new List<string>();
            var recorder = new RecordingUIService(order);
            var recordedScope = new ScopedViewRegistration(recorder);

            recordedScope.Register<ScreenA>().GetAwaiter().GetResult();
            recordedScope.Register<PopupA>().GetAwaiter().GetResult();
            recordedScope.Register<HudA>().GetAwaiter().GetResult();

            recordedScope.Dispose();

            Assert.AreEqual(new[] { nameof(HudA), nameof(PopupA), nameof(ScreenA) }, order);
        }

        [Test]
        public void Dispose_CleanupAction_Throws_AllOtherCleanupsStillRun()
        {
            var order = new List<string>();
            var recorder = new RecordingUIService(order) { FailOnUnregisterType = typeof(PopupA) };
            var scope = new ScopedViewRegistration(recorder);

            scope.Register<ScreenA>().GetAwaiter().GetResult();
            scope.Register<PopupA>().GetAwaiter().GetResult();
            scope.Register<HudA>().GetAwaiter().GetResult();

            Assert.Throws<AggregateException>(() => scope.Dispose());
            Assert.AreEqual(new[] { nameof(HudA), nameof(ScreenA) }, order);
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

            public UniTask Register<T>() where T : View => UniTask.CompletedTask;

            public void Unregister<T>() where T : View
            {
                if (FailOnUnregisterType == typeof(T))
                    throw new InvalidOperationException($"Fail on {typeof(T).Name}");
                _order.Add(typeof(T).Name);
            }

            public T Get<T>() where T : View => throw new NotSupportedException();
            public UniTask Show<T>(ViewModelBase viewModel) where T : View => UniTask.CompletedTask;
            public void Hide<T>() where T : View { }
            public UniTask HideAsync<T>() where T : View => UniTask.CompletedTask;
            public void HideTop() { }
            public UniTask HideTopAsync() => UniTask.CompletedTask;
            public void HideAll() { }
            public UniTask HideAllAsync() => UniTask.CompletedTask;
            public IDisposable CapturePointer() => throw new NotSupportedException();
            public ReadOnlyReactiveProperty<bool> PointerCaptured => throw new NotSupportedException();
            public IDisposable PushBackHandler(Func<bool> handler) => throw new NotSupportedException();
            public bool Back() => throw new NotSupportedException();
        }
    }
}
