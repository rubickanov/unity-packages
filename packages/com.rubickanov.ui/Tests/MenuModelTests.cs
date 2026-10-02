using System.Collections.Generic;
using NUnit.Framework;
using R3;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class MenuModelTests
    {
        private readonly List<string> _chosen = new();

        [SetUp]
        public void SetUp() => _chosen.Clear();

        private MenuEntry Item(string text, bool enabled = true) => new(text, () => _chosen.Add(text), enabled);

        private MenuModel Menu(MenuNavigation? navigation, params bool[] enabled)
        {
            var items = new List<MenuEntry>();
            for (var i = 0; i < enabled.Length; i++) items.Add(Item($"item {i}", enabled[i]));
            return new MenuModel(items, navigation);
        }

        [Test]
        public void Focus_SomeItemsOff_StartsOnFirstOnAndStepsOverTheOff()
        {
            using var menu = Menu(null, false, true, false, true);
            var start = menu.Focus.CurrentValue;

            menu.Step(MenuStep.Down);
            var afterDown = menu.Focus.CurrentValue;
            menu.Step(MenuStep.Down);
            var atEnd = menu.Focus.CurrentValue;
            menu.Step(MenuStep.Up);

            Assert.AreEqual(1, start);
            Assert.AreEqual(3, afterDown);
            Assert.AreEqual(3, atEnd, "no wrapping round");
            Assert.AreEqual(1, menu.Focus.CurrentValue);
        }

        [Test]
        public void Submit_FocusedItem_PressedThenChosen()
        {
            using var menu = Menu(null, true, true, true);
            var pressed = new List<int>();
            using var subscription = menu.Pressed.Subscribe(pressed.Add);

            menu.Step(MenuStep.Down);
            menu.Submit();

            CollectionAssert.AreEqual(new[] { 1 }, pressed);
            CollectionAssert.AreEqual(new[] { "item 1" }, _chosen);
        }

        [Test]
        public void PointAndClick_ItemOff_NeitherFocusesNorChooses()
        {
            using var menu = Menu(null, true, false, true);

            menu.Point(1);
            var afterPoint = menu.Focus.CurrentValue;
            menu.Click(1);
            menu.Click(2);

            Assert.AreEqual(0, afterPoint);
            Assert.AreEqual(2, menu.Focus.CurrentValue);
            CollectionAssert.AreEqual(new[] { "item 2" }, _chosen);
        }

        [Test]
        public void Focus_NothingOn_NoFocusAndNothingPicked()
        {
            using var menu = Menu(null, false, false);

            menu.Step(MenuStep.Down);
            menu.Submit();

            Assert.AreEqual(-1, menu.Focus.CurrentValue);
            CollectionAssert.IsEmpty(_chosen);
        }

        [Test]
        public void Ctor_WithNavigation_HearsTheKeysWhileAlive()
        {
            using var navigation = new MenuNavigation();
            var menu = Menu(navigation, true, true);
            var claimedWhileAlive = navigation.Current;

            menu.Dispose();

            Assert.AreSame(menu, claimedWhileAlive);
            Assert.IsNull(navigation.Current);
        }

        [Test]
        public void Ctor_ClaimFalse_LeavesTheKeysToItsOwner()
        {
            using var navigation = new MenuNavigation();

            using var menu = new MenuModel(new[] { Item("a") }, navigation, claim: false);

            Assert.IsNull(navigation.Current);
        }

        [Test]
        public void Cues_FocusPickAndItemOff_SoundedInOrder()
        {
            using var navigation = new MenuNavigation();
            var cues = new List<MenuCue>();
            using var subscription = navigation.Cues.Subscribe(cues.Add);
            using var menu = Menu(navigation, true, true, false);

            menu.Step(MenuStep.Down);
            menu.Submit();
            menu.Choose(2);

            CollectionAssert.AreEqual(new[] { MenuCue.Focus, MenuCue.Select, MenuCue.Denied }, cues);
        }

        [Test]
        public void Cues_ItemWithBackCueAndSilentItem_SoundAsTheySay()
        {
            using var navigation = new MenuNavigation();
            var cues = new List<MenuCue>();
            using var subscription = navigation.Cues.Subscribe(cues.Add);
            using var menu = new MenuModel(new[] { Item("STAY").WithCue(MenuCue.Back), Item("QUIET").WithCue(null) },
                navigation);

            menu.Choose(0);
            menu.Choose(1);

            CollectionAssert.AreEqual(new[] { MenuCue.Back }, cues);
            CollectionAssert.AreEqual(new[] { "STAY", "QUIET" }, _chosen);
        }

        [Test]
        public void Point_WithNavigation_ReportsThePointer()
        {
            using var navigation = new MenuNavigation();
            var pointed = 0;
            using var subscription = navigation.Pointed.Subscribe(_ => pointed++);
            using var menu = Menu(navigation, true, true);

            menu.Point(1);

            Assert.AreEqual(1, pointed);
        }

        [Test]
        public void Steps_FromNavigation_MoveTheFocus()
        {
            using var navigation = new MenuNavigation();
            using var menu = Menu(navigation, true, true);

            navigation.Update(default, 1f);
            navigation.Update(new MenuInput { Navigate = UnityEngine.Vector2.down }, 1.02f);

            Assert.AreEqual(1, menu.Focus.CurrentValue);
        }
    }
}
