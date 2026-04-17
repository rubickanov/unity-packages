using System;
using NUnit.Framework;
using R3;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class ViewModelBaseTests
    {
        private sealed class SampleViewModel : ViewModelBase
        {
            public ReactiveProperty<int> Score { get; }
            public ReactiveCommand Action { get; }
            public ReactiveCommand<string> Typed { get; }
            public Subject<int> Event { get; }

            public SampleViewModel()
            {
                Score = CreateProperty(10);
                Action = CreateCommand();
                Typed = CreateCommand<string>();
                Event = CreateSubject<int>();
            }

            public new void AddDisposable(IDisposable d) => base.AddDisposable(d);
        }

        private sealed class DisposeCounter : IDisposable
        {
            public int Disposes;
            public void Dispose() => Disposes++;
        }

        [Test]
        public void CreateProperty_InitialValue_IsReturnedByProperty()
        {
            using var vm = new SampleViewModel();

            Assert.AreEqual(10, vm.Score.Value);
        }

        [Test]
        public void Dispose_DisposesCreatedProperty_NoFurtherNotifications()
        {
            var vm = new SampleViewModel();
            var property = vm.Score;
            int observed = 0;
            var sub = property.Subscribe(_ => observed++);
            int beforeDispose = observed;

            vm.Dispose();
            try { property.Value = 99; } catch { /* disposed property may throw */ }

            Assert.AreEqual(beforeDispose, observed,
                "Subscriber must not receive notifications after VM disposal.");
            sub.Dispose();
        }

        [Test]
        public void Dispose_DisposesCreatedCommand_NoFurtherExecution()
        {
            var vm = new SampleViewModel();
            int calls = 0;
            vm.Action.Subscribe(_ => calls++);

            vm.Dispose();
            try { vm.Action.Execute(Unit.Default); } catch { /* command may throw after dispose */ }

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Dispose_DisposesCreatedTypedCommand_NoFurtherExecution()
        {
            var vm = new SampleViewModel();
            int calls = 0;
            vm.Typed.Subscribe(_ => calls++);

            vm.Dispose();
            try { vm.Typed.Execute("x"); } catch { /* may throw after dispose */ }

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Dispose_DisposesCreatedSubject_NoFurtherNotifications()
        {
            var vm = new SampleViewModel();
            var subject = vm.Event;
            int observed = 0;
            var sub = subject.Subscribe(_ => observed++);

            vm.Dispose();
            try { subject.OnNext(42); } catch { /* disposed subject may throw */ }

            Assert.AreEqual(0, observed);
            sub.Dispose();
        }

        [Test]
        public void Dispose_DisposesTrackedExternalDisposable()
        {
            var vm = new SampleViewModel();
            var tracked = new DisposeCounter();
            vm.AddDisposable(tracked);

            vm.Dispose();

            Assert.AreEqual(1, tracked.Disposes);
        }

        [Test]
        public void Dispose_CalledTwice_IsSafe()
        {
            var vm = new SampleViewModel();
            var tracked = new DisposeCounter();
            vm.AddDisposable(tracked);

            vm.Dispose();
            Assert.DoesNotThrow(() => vm.Dispose());
            Assert.AreEqual(1, tracked.Disposes,
                "Tracked disposable should only fire once on repeated Dispose.");
        }

        [Test]
        public void TrackDisposable_Public_DisposedWithViewModel()
        {
            var vm = new SampleViewModel();
            var tracked = new DisposeCounter();
            vm.TrackDisposable(tracked);

            vm.Dispose();

            Assert.AreEqual(1, tracked.Disposes);
        }

        [Test]
        public void CreateCommand_WithHandler_InvokesOnExecute()
        {
            using var vm = new SampleViewModel();
            int called = 0;
            var viaHandler = new HandlerVM(() => called++);

            viaHandler.Action.Execute(Unit.Default);

            Assert.AreEqual(1, called);
            viaHandler.Dispose();
        }

        private sealed class HandlerVM : ViewModelBase
        {
            public ReactiveCommand Action { get; }
            public HandlerVM(Action handler) { Action = CreateCommand(handler); }
        }

        private sealed class OnDisposeCounter : ViewModelBase
        {
            public int OnDisposeCalls;
            protected override void OnDispose() => OnDisposeCalls++;
        }

        [Test]
        public void OnDispose_CalledOnceOnFirstDispose()
        {
            var vm = new OnDisposeCounter();

            vm.Dispose();
            vm.Dispose();

            Assert.AreEqual(1, vm.OnDisposeCalls);
        }
    }
}
