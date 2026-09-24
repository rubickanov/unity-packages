using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ObservableCollections;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class ListBindingTests
    {
        private UIService _ui = null!;
        private ListScreen _view = null!;
        private ObservableList<string> _items = null!;

        [SetUp]
        public void SetUp()
        {
            // Code-only views register synchronously.
            _ui = new UIService(TestRoot.Create(), new RecordingUxmlLoader().Load);
            _ui.Register<ListScreen>().GetAwaiter().GetResult();
            _ui.Register<UnlistedRowScreen>().GetAwaiter().GetResult();
            _view = _ui.Get<ListScreen>();
            _items = new ObservableList<string>();
        }

        [TearDown]
        public void TearDown() => _ui?.Dispose();

        private async Task Show(params string[] items)
        {
            _items.AddRange(items);
            await _ui.Show<ListScreen>(new ListViewModel(_items));
        }

        /// <summary>Names of the container's children: rows are named after their item.</summary>
        private string[] Children => _view.Rows.Children().Select(e => e.name).ToArray();

        private RowViewModel RowViewModelOf(string item) =>
            _view.Created.Single(vm => vm.Item == item && vm.DisposeCalls == 0);

        [Test]
        public async Task Bind_ExistingItems_OneRowPerItemAfterOtherElements()
        {
            await Show("a", "b", "c");

            CollectionAssert.AreEqual(new[] { "header", "a", "b", "c" }, Children);
        }

        [Test]
        public async Task Bind_EmptySource_OnlyOtherElements()
        {
            await Show();

            CollectionAssert.AreEqual(new[] { "header" }, Children);
        }

        [Test]
        public async Task Add_ToEmptySource_RowGoesAfterOtherElements()
        {
            await Show();

            _items.Add("a");

            CollectionAssert.AreEqual(new[] { "header", "a" }, Children);
        }

        [Test]
        public async Task Insert_Item_RowInsertedAtIndex()
        {
            await Show("a", "c");

            _items.Insert(1, "b");

            CollectionAssert.AreEqual(new[] { "header", "a", "b", "c" }, Children);
        }

        [Test]
        public async Task AddRange_Items_RowsAppendedInOrder()
        {
            await Show("a");

            _items.AddRange(new[] { "b", "c" });

            CollectionAssert.AreEqual(new[] { "header", "a", "b", "c" }, Children);
        }

        [Test]
        public async Task Remove_Item_RowDestroyedAndViewModelDisposed()
        {
            await Show("a", "b", "c");
            var removed = RowViewModelOf("b");

            _items.Remove("b");

            CollectionAssert.AreEqual(new[] { "header", "a", "c" }, Children);
            Assert.AreEqual(1, removed.DisposeCalls);
        }

        [Test]
        public async Task RemoveRange_Items_RowsDestroyed()
        {
            await Show("a", "b", "c", "d");

            _items.RemoveRange(1, 2);

            CollectionAssert.AreEqual(new[] { "header", "a", "d" }, Children);
        }

        [Test]
        public async Task Replace_Item_NewRowInSamePlace()
        {
            await Show("a", "b", "c");
            var replaced = RowViewModelOf("b");

            _items[1] = "x";

            CollectionAssert.AreEqual(new[] { "header", "a", "x", "c" }, Children);
            Assert.AreEqual(1, replaced.DisposeCalls);
        }

        [Test]
        public async Task Move_Item_RowMovesAndKeepsViewModel()
        {
            await Show("a", "b", "c");
            var moved = RowViewModelOf("a");

            _items.Move(0, 2);

            CollectionAssert.AreEqual(new[] { "header", "b", "c", "a" }, Children);
            Assert.AreEqual(0, moved.DisposeCalls);
            Assert.AreEqual(3, _view.Created.Count);
        }

        [Test]
        public async Task Sort_Items_RowsReorderedAndKept()
        {
            await Show("c", "a", "b");

            _items.Sort();

            CollectionAssert.AreEqual(new[] { "header", "a", "b", "c" }, Children);
            Assert.AreEqual(3, _view.Created.Count);
            Assert.IsTrue(_view.Created.All(vm => vm.DisposeCalls == 0));
        }

        [Test]
        public async Task Clear_Items_EveryRowDisposed()
        {
            await Show("a", "b");

            _items.Clear();

            CollectionAssert.AreEqual(new[] { "header" }, Children);
            Assert.IsTrue(_view.Created.All(vm => vm.DisposeCalls == 1));
        }

        [Test]
        public async Task HideParent_RowsDisposedAndSourceNoLongerFollowed()
        {
            await Show("a");

            _ui.Hide<ListScreen>();
            _items.Add("b");

            Assert.AreEqual(1, _view.Created.Count);
            Assert.AreEqual(1, _view.Created[0].DisposeCalls);
            CollectionAssert.AreEqual(new[] { "header" }, Children);
        }

        [Test]
        public void Bind_UnlistedRowType_ThrowsNamingChildViews()
        {
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _ui.Show<UnlistedRowScreen>(new ListViewModel(_items)));

            StringAssert.Contains("ChildViews", ex.Message);
        }

        [Test]
        public async Task Reset_FactoryThrows_UnmatchedRowsKeptAndClearRemovesThem()
        {
            await Show("a", "b", "c");
            _view.ThrowFor = "boom";
            Assert.Throws<InvalidOperationException>(() => _items.Insert(0, "boom"));

            Assert.Throws<InvalidOperationException>(() => _items.Sort(StringComparer.Ordinal));
            var afterSort = Children;
            _items.Clear();

            CollectionAssert.AreEqual(new[] { "header", "a", "b", "c" }, afterSort);
            CollectionAssert.AreEqual(new[] { "header" }, Children);
            Assert.IsTrue(_view.Created.All(vm => vm.DisposeCalls == 1));
        }

        public sealed class ListViewModel : ViewModelBase
        {
            public readonly ObservableList<string> Items;
            public ListViewModel(ObservableList<string> items) => Items = items;
        }

        public sealed class RowViewModel : ViewModelBase
        {
            public readonly string Item;
            public int DisposeCalls { get; private set; }
            public RowViewModel(string item) => Item = item;
            protected override void OnDispose() => DisposeCalls++;
        }

        public sealed class RowView : View<RowViewModel>
        {
            protected override string? UxmlName => null;
            protected override UILayer Layer => UILayer.HUD;
            protected override void OnBind() => Root.name = ViewModel.Item;
        }

        public sealed class ListScreen : View<ListViewModel>
        {
            public readonly List<RowViewModel> Created = new();
            public VisualElement Rows { get; private set; } = null!;
            public string? ThrowFor;

            protected override string? UxmlName => null;
            protected override UILayer Layer => UILayer.Screen;
            protected override IReadOnlyList<Type> ChildViews => new[] { typeof(RowView) };

            protected override void OnInitialize()
            {
                Rows = new VisualElement();
                Rows.Add(new VisualElement { name = "header" });
                Root.Add(Rows);
            }

            protected override void OnBind()
            {
                BindList<string, RowView, RowViewModel>(ViewModel.Items, Rows, item =>
                {
                    if (item == ThrowFor) throw new InvalidOperationException("factory");
                    var vm = new RowViewModel(item);
                    Created.Add(vm);
                    return vm;
                });
            }
        }

        public sealed class UnlistedRowScreen : View<ListViewModel>
        {
            protected override string? UxmlName => null;
            protected override UILayer Layer => UILayer.Screen;

            protected override void OnBind() =>
                BindList<string, RowView, RowViewModel>(ViewModel.Items, Root, item => new RowViewModel(item));
        }
    }
}
