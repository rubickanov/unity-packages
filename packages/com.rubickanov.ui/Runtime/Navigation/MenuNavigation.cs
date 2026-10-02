using System;
using System.Collections.Generic;
using R3;

namespace Rubickanov.UI
{
    /// <summary>
    /// Keyboard and pad navigation for menus, with no input reading of its own: the game reads its actions once a frame
    /// into a <see cref="MenuInput"/> and calls <see cref="Update"/>. A direction steps the focus once on a press and
    /// again while held (<see cref="StepRepeat"/>), the page axis likewise, a tab once a press, submit presses the
    /// focused item. Only the menu that claimed last (<see cref="Claim"/>) hears them, so a menu opened over another
    /// takes over and gives it back when it goes; a direction already held when a menu claims waits for its release,
    /// so a key held into a pause menu does not walk it. Menus tell what they did through <see cref="Cue"/>, for sounds.
    /// </summary>
    public sealed class MenuNavigation : IDisposable
    {
        private readonly List<IMenuTarget> _claims = new();
        private readonly StepRepeat _repeat;
        private readonly StepRepeat _pageRepeat;
        private readonly Subject<MenuCue> _cues = new();
        private readonly Subject<Unit> _pointed = new();

        public MenuNavigation(float repeatDelay = StepRepeat.DefaultDelay, float repeatInterval = StepRepeat.DefaultInterval)
        {
            _repeat = new StepRepeat(repeatDelay, repeatInterval);
            _pageRepeat = new StepRepeat(repeatDelay, repeatInterval);
        }

        /// <summary>The menu that hears the steps and presses now, if any.</summary>
        public IMenuTarget? Current => _claims.Count > 0 ? _claims[^1] : null;

        /// <summary>What the menus did, as they did it.</summary>
        public Observable<MenuCue> Cues => _cues;

        /// <summary>The pointer moved over or clicked a menu: a game shows the keyboard's hints, say.</summary>
        public Observable<Unit> Pointed => _pointed;

        /// <summary>A menu did <paramref name="cue"/>.</summary>
        public void Cue(MenuCue cue) => _cues.OnNext(cue ?? throw new ArgumentNullException(nameof(cue)));

        /// <summary>A menu was pointed at with the mouse.</summary>
        public void Point() => _pointed.OnNext(Unit.Default);

        /// <summary>Gives <paramref name="target"/> the steps and presses until the handle is disposed.</summary>
        public IDisposable Claim(IMenuTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            _claims.Add(target);
            WaitForRelease();
            return new Handle(this, target);
        }

        /// <summary>
        /// The direction and page held now count for nothing until they are let go. Call it, instead of
        /// <see cref="Update"/>, every frame the game's menu input is off (a console typing), so a key held through it
        /// does not step when the input comes back.
        /// </summary>
        public void WaitForRelease()
        {
            _repeat.WaitForRelease();
            _pageRepeat.WaitForRelease();
        }

        /// <summary>One frame of input at <paramref name="now"/>, unscaled seconds: steps and presses the current menu.</summary>
        public void Update(in MenuInput input, float now)
        {
            var stepped = _repeat.Next(StepRepeat.Direction(input.Navigate), now, out var step);
            var paged = _pageRepeat.Next(StepRepeat.Along(input.Page, MenuStep.PreviousPage, MenuStep.NextPage), now,
                out var page);

            var target = Current;
            if (target == null) return;

            if (stepped) target.Step(step);
            if (paged && Current == target) target.Step(page);
            if (input.Tab != 0 && Current == target) target.Step(input.Tab < 0 ? MenuStep.PreviousTab : MenuStep.NextTab);
            if (input.Submit && Current == target) target.Submit();
        }

        public void Dispose()
        {
            _claims.Clear();
            _cues.Dispose();
            _pointed.Dispose();
        }

        private void Release(IMenuTarget target)
        {
            // The last claim of this target: a menu may claim again while an older claim of its still stands.
            var index = _claims.LastIndexOf(target);
            if (index >= 0) _claims.RemoveAt(index);
        }

        private sealed class Handle : IDisposable
        {
            private MenuNavigation? _owner;
            private readonly IMenuTarget _target;

            public Handle(MenuNavigation owner, IMenuTarget target)
            {
                _owner = owner;
                _target = target;
            }

            public void Dispose()
            {
                var owner = _owner;
                _owner = null;
                owner?.Release(_target);
            }
        }
    }
}
