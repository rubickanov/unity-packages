using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Rubickanov.UI;

namespace Rubickanov.UI.Loading.Tests
{
    [TestFixture]
    public class RegisterViewsOperationTests
    {
        private UIService _ui = null!;
        private SceneViewScopeService _scope = null!;

        [SetUp]
        public void SetUp()
        {
            _ui = new UIService(TestRoot.Create(), TestRoot.NoUxml);
            _scope = new SceneViewScopeService(_ui);
        }

        [TearDown]
        public void TearDown()
        {
            _scope?.Dispose();
            _ui?.Dispose();
        }

        [Test]
        public void Ctor_NullScopeService_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new RegisterViewsOperation(null!));
        }

        [Test]
        public void Ctor_NullDescription_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new RegisterViewsOperation(_scope, null!));
        }

        [Test]
        public void Ctor_DefaultDescription_IsLoadingUi()
        {
            var op = new RegisterViewsOperation(_scope);

            Assert.AreEqual("Loading UI...", op.Description);
        }

        [Test]
        public void Ctor_CustomDescription_IsPreserved()
        {
            var op = new RegisterViewsOperation(_scope, "Custom");

            Assert.AreEqual("Custom", op.Description);
        }

        [Test]
        public void Add_DuplicateType_Throws()
        {
            var op = new RegisterViewsOperation(_scope).Add<FakeViewA>();

            Assert.Throws<InvalidOperationException>(() => op.Add<FakeViewA>());
        }

        [Test]
        public async Task Execute_RegistersAllViews_ResolvableOnUiService()
        {
            var op = new RegisterViewsOperation(_scope)
                .Add<FakeViewA>()
                .Add<FakeViewB>();

            await op.Execute(new DummyProgress(), CancellationToken.None);

            Assert.DoesNotThrow(() => _ui.Get<FakeViewA>());
            Assert.DoesNotThrow(() => _ui.Get<FakeViewB>());
        }

        [Test]
        public async Task Execute_ReportsProgress_FromZeroToOne()
        {
            var op = new RegisterViewsOperation(_scope)
                .Add<FakeViewA>()
                .Add<FakeViewB>();
            var progress = new RecordingProgress();

            await op.Execute(progress, CancellationToken.None);

            Assert.AreEqual(0f, progress.Values[0]);
            Assert.AreEqual(1f, progress.Values[^1]);
        }

        [Test]
        public async Task Execute_EmptyOperation_ReportsOne()
        {
            var op = new RegisterViewsOperation(_scope);
            var progress = new RecordingProgress();

            await op.Execute(progress, CancellationToken.None);

            Assert.AreEqual(1f, progress.Values[^1]);
        }

        [Test]
        public async Task Execute_Twice_Throws()
        {
            var op = new RegisterViewsOperation(_scope).Add<FakeViewA>();
            await op.Execute(new DummyProgress(), CancellationToken.None);

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await op.Execute(new DummyProgress(), CancellationToken.None).AsTask());
        }

        [Test]
        public void Execute_CancelledBeforeStart_Throws()
        {
            var op = new RegisterViewsOperation(_scope).Add<FakeViewA>();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await op.Execute(new DummyProgress(), cts.Token).AsTask());
        }

        [Test]
        public async Task Execute_CancelledBeforeBegin_LeavesPriorScopeIntact()
        {
            var first = new RegisterViewsOperation(_scope).Add<FakeViewA>();
            await first.Execute(new DummyProgress(), CancellationToken.None);

            var second = new RegisterViewsOperation(_scope).Add<FakeViewB>();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await second.Execute(new DummyProgress(), cts.Token).AsTask());

            Assert.DoesNotThrow(() => _ui.Get<FakeViewA>());
        }

        [Test]
        public void Execute_ScopeReplacedDuringLoad_LeavesNoViewOfOldScopeRegistered()
        {
            var loader = new DeferredUxmlLoader();
            using var ui = new UIService(TestRoot.Create(), loader.Load);
            using var scopeService = new SceneViewScopeService(ui);
            var op = new RegisterViewsOperation(scopeService)
                .Add<FakeViewA>()
                .Add<UxmlViewA>()
                .Add<FakeViewB>();
            var execution = op.Execute(new DummyProgress(), CancellationToken.None);

            scopeService.Begin();
            loader.Complete(nameof(UxmlViewA));

            Assert.CatchAsync<OperationCanceledException>(async () => await execution);
            Assert.Throws<InvalidOperationException>(() => ui.Get<FakeViewA>());
            Assert.Throws<InvalidOperationException>(() => ui.Get<UxmlViewA>());
            Assert.Throws<InvalidOperationException>(() => ui.Get<FakeViewB>());
        }

        private sealed class DummyProgress : IProgress<float>
        {
            public void Report(float value) { }
        }

        private sealed class RecordingProgress : IProgress<float>
        {
            public readonly List<float> Values = new();
            public void Report(float value) => Values.Add(value);
        }
    }
}
