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
        private FakeViewFactory _factory = null!;
        private UIService _ui = null!;
        private SceneViewScopeService _scope = null!;

        [SetUp]
        public void SetUp()
        {
            _factory = new FakeViewFactory();
            _ui = new UIService(_factory);
            _scope = new SceneViewScopeService(_ui);

            _factory.Preset<FakeViewA>(new FakeViewA());
            _factory.Preset<FakeViewB>(new FakeViewB());
            _factory.Preset<FakeViewC>(new FakeViewC());
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
            var op = new RegisterViewsOperation(_scope).Add<FakeViewA>(UILayer.Screen);

            Assert.Throws<InvalidOperationException>(() => op.Add<FakeViewA>(UILayer.Popup));
        }

        [Test]
        public async Task Execute_RegistersAllViews_ResolvableOnUiService()
        {
            var op = new RegisterViewsOperation(_scope)
                .Add<FakeViewA>(UILayer.Screen)
                .Add<FakeViewB>(UILayer.Popup);

            await op.Execute(new DummyProgress(), CancellationToken.None);

            Assert.DoesNotThrow(() => _ui.Get<FakeViewA>());
            Assert.DoesNotThrow(() => _ui.Get<FakeViewB>());
        }

        [Test]
        public async Task Execute_ReportsProgress_FromZeroToOne()
        {
            var op = new RegisterViewsOperation(_scope)
                .Add<FakeViewA>(UILayer.Screen)
                .Add<FakeViewB>(UILayer.Popup);
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
            var op = new RegisterViewsOperation(_scope).Add<FakeViewA>(UILayer.Screen);
            await op.Execute(new DummyProgress(), CancellationToken.None);

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await op.Execute(new DummyProgress(), CancellationToken.None).AsTask());
        }

        [Test]
        public void Execute_CancelledBeforeStart_Throws()
        {
            var op = new RegisterViewsOperation(_scope).Add<FakeViewA>(UILayer.Screen);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await op.Execute(new DummyProgress(), cts.Token).AsTask());
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
