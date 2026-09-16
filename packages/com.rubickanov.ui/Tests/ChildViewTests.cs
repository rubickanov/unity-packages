using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class ChildViewTests
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
        public async Task Register_ParentWithChildViews_LoadsChildUxml()
        {
            await _ui.Register<ParentView>();

            CollectionAssert.AreEqual(new[] { nameof(UxmlChildView) }, _loader.Loaded);
        }

        [Test]
        public async Task CreateChild_ListedType_AddedToContainerBoundAndShown()
        {
            await _ui.Register<ParentView>();
            var parent = _ui.Get<ParentView>();
            await _ui.Show<ParentView>(new FakeViewModel());
            var childVm = new FakeViewModel();

            var child = parent.AddUxmlChild(childVm);

            Assert.AreSame(parent.Root, child.Root.parent);
            Assert.AreEqual(ViewState.Shown, child.State);
            Assert.AreSame(childVm, child.LastViewModel);
            CollectionAssert.AreEqual(new[] { nameof(UxmlChildView) }, _loader.Loaded);
        }

        [Test]
        public async Task CreateChild_UnlistedType_ThrowsNamingChildViews()
        {
            await _ui.Register<ParentView>();
            var parent = _ui.Get<ParentView>();
            await _ui.Show<ParentView>(new FakeViewModel());

            var ex = Assert.Throws<InvalidOperationException>(() => parent.AddUnlistedChild(new FakeViewModel()));

            StringAssert.Contains("ChildViews", ex.Message);
            StringAssert.Contains(nameof(CodeChildView), ex.Message);
        }

        [Test]
        public async Task HideParent_ChildViewModelDisposedAndChildDetached()
        {
            await _ui.Register<ParentView>();
            var parent = _ui.Get<ParentView>();
            await _ui.Show<ParentView>(new FakeViewModel());
            var childVm = new FakeViewModel();
            var child = parent.AddUxmlChild(childVm);

            _ui.Hide<ParentView>();

            Assert.AreEqual(1, childVm.DisposeCalls);
            Assert.IsNull(child.Root.parent);
            Assert.AreEqual(ViewState.Hidden, child.State);
        }

        [Test]
        public async Task ShowParent_NewViewModel_ChildrenOfOldBindingDestroyed()
        {
            await _ui.Register<ParentView>();
            var parent = _ui.Get<ParentView>();
            await _ui.Show<ParentView>(new FakeViewModel());
            var childVm = new FakeViewModel();
            var child = parent.AddUxmlChild(childVm);

            await _ui.Show<ParentView>(new FakeViewModel());

            Assert.AreEqual(1, childVm.DisposeCalls);
            Assert.IsNull(child.Root.parent);
        }

        [Test]
        public async Task Unregister_Parent_ReleasesChildUxml()
        {
            await _ui.Register<ParentView>();

            _ui.Unregister<ParentView>();

            CollectionAssert.AreEqual(new[] { nameof(UxmlChildView) }, _loader.Released);
        }

        public sealed class ParentView : FakeView
        {
            protected override UILayer Layer => UILayer.Screen;
            protected override IReadOnlyList<Type> ChildViews => new[] { typeof(UxmlChildView) };

            public UxmlChildView AddUxmlChild(FakeViewModel vm) => CreateChild<UxmlChildView, FakeViewModel>(vm, Root);
            public CodeChildView AddUnlistedChild(FakeViewModel vm) => CreateChild<CodeChildView, FakeViewModel>(vm, Root);
        }

        public sealed class UxmlChildView : View<FakeViewModel>
        {
            public FakeViewModel? LastViewModel { get; private set; }
            protected override UILayer Layer => UILayer.HUD;
            protected override void OnBind() => LastViewModel = ViewModel;
        }

        public sealed class CodeChildView : View<FakeViewModel>
        {
            protected override string? UxmlName => null;
            protected override UILayer Layer => UILayer.HUD;
            protected override void OnBind() { }
        }
    }
}
