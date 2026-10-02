using System;
using System.Collections.Generic;
using R3;

namespace Rubickanov.UI
{
    /// <summary>
    /// A vertical menu's state: its items, the focused one and the presses, for a view to draw. Up and Down move the
    /// focus over the items that are on, without wrapping round; Submit picks the focused one; the pointer focuses what
    /// it is over (<see cref="Point"/>) and picks what it clicks (<see cref="Click"/>). While alive it holds a claim on
    /// <see cref="MenuNavigation"/>, so the menu on top hears the keys, and cues what it did for the sound: the focus
    /// moving, an item picked (as the item sounds), an item that is off picked. Part of its view model, disposed with it.
    /// </summary>
    public sealed class MenuModel : IMenuTarget, IDisposable
    {
        private readonly List<MenuEntry> _items;
        private readonly MenuNavigation? _navigation;
        private readonly ReactiveProperty<int> _focus;
        private readonly Subject<int> _pressed = new();
        private readonly IDisposable? _claim;

        /// <param name="navigation">Where the keys come from and the cues go; null for none, as in tests.</param>
        /// <param name="claim">
        /// False for a menu inside a view model that claims the keys itself and hands them on: it only cues.
        /// </param>
        public MenuModel(IEnumerable<MenuEntry> items, MenuNavigation? navigation, bool claim = true)
        {
            _items = new List<MenuEntry>(items ?? throw new ArgumentNullException(nameof(items)));
            _navigation = navigation;
            _focus = new ReactiveProperty<int>(Next(-1, 1));
            _claim = claim ? navigation?.Claim(this) : null;
        }

        public IReadOnlyList<MenuEntry> Items => _items;

        /// <summary>The focused item; -1 while no item is on.</summary>
        public ReadOnlyReactiveProperty<int> Focus => _focus;

        /// <summary>An item picked, for the press to show.</summary>
        public Observable<int> Pressed => _pressed;

        public void Step(MenuStep step)
        {
            var direction = step switch
            {
                MenuStep.Up => -1,
                MenuStep.Down => 1,
                _ => 0,
            };
            if (direction == 0 || _focus.Value < 0) return;

            var next = Next(_focus.Value, direction);
            if (next >= 0) MoveFocus(next);
        }

        public void Submit() => Choose(_focus.Value);

        /// <summary>The pointer is over <paramref name="index"/>: it takes the focus if it is on.</summary>
        public void Point(int index)
        {
            _navigation?.Point();
            if (IsOn(index)) MoveFocus(index);
        }

        /// <summary>The pointer clicked <paramref name="index"/>: focused and picked if it is on.</summary>
        public void Click(int index)
        {
            Point(index);
            Choose(index);
        }

        /// <summary>
        /// Picks <paramref name="index"/> if it is on: the press shows, the item sounds, then it does what it does. One
        /// that is off only cues <see cref="MenuCue.Denied"/>.
        /// </summary>
        public void Choose(int index)
        {
            if (index < 0 || index >= _items.Count) return;

            var item = _items[index];
            if (!item.Enabled)
            {
                _navigation?.Cue(MenuCue.Denied);
                return;
            }

            _pressed.OnNext(index);
            if (item.Cue != null) _navigation?.Cue(item.Cue);
            item.Choose();
        }

        public void Dispose()
        {
            _claim?.Dispose();
            _focus.Dispose();
            _pressed.Dispose();
        }

        private bool IsOn(int index) => index >= 0 && index < _items.Count && _items[index].Enabled;

        private void MoveFocus(int index)
        {
            if (index == _focus.Value) return;

            _focus.Value = index;
            _navigation?.Cue(MenuCue.Focus);
        }

        // The next item that is on from `from` in `direction`, or -1.
        private int Next(int from, int direction)
        {
            for (var i = from + direction; i >= 0 && i < _items.Count; i += direction)
            {
                if (_items[i].Enabled) return i;
            }
            return -1;
        }
    }
}
