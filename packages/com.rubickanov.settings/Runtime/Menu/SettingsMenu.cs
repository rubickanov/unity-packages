using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace Rubickanov.Settings
{
    public enum SettingsStep
    {
        Up,
        Down,
        Left,
        Right,
        PreviousSection,
        NextSection,
    }

    /// <summary>What the menu did, for the game's sounds.</summary>
    public enum SettingsCue
    {
        /// <summary>The focus moved (a row, a key of a keys row, a question's answer).</summary>
        Focus,

        /// <summary>A value changed.</summary>
        Change,

        /// <summary>An action was done, a question answered yes, a listening started.</summary>
        Select,

        /// <summary>A question answered no, a listening called off.</summary>
        Back,

        /// <summary>A row that is off was picked, a key could not be listened for.</summary>
        Denied,

        /// <summary>Another section.</summary>
        Section,
    }

    /// <summary>What a row may ask of the menu it is in.</summary>
    public interface ISettingsMenu
    {
        ISettingsText Text { get; }

        void Ask(SettingsQuestion question);

        void Cue(SettingsCue cue);

        /// <summary>
        /// The section's rows made again, the focus and a keys row's picked key kept by row id; while a key is listened
        /// for, once it comes.
        /// </summary>
        void Rebuild();
    }

    /// <summary>
    /// A settings screen without the drawing: sections of rows, the focus, and what the menu waits on (an answer, a new
    /// key). Up and down move the focus past headings and rows that are off or hidden, left and right change the value
    /// (none go round), the section keys change the section, Enter acts; Back first answers a question no or calls a
    /// listening off, otherwise it is the owner's to close the menu. A keep-or-revert choice only picks; the section's
    /// APPLY puts its picks on and asks to keep them, going back after <see cref="KeepSeconds"/> unanswered; picks not
    /// applied are dropped when the section opens again. A keep-or-revert row of another kind asks on each change. <see cref="Changed"/> fires on anything shown.
    /// </summary>
    public sealed class SettingsMenu : ISettingsMenu, IDisposable
    {
        public const float KeepSeconds = 10f;

        private readonly SettingsLayout _layout;
        private readonly Func<float> _now;
        private readonly Action<string> _logWarning;
        private readonly List<SettingsRow> _all = new();
        private readonly List<SettingsRow> _rows = new();
        private readonly List<object?> _keepBefore = new();
        private readonly List<SettingsRow> _keepRows = new();
        private readonly List<Action?> _keepRestore = new();
        private readonly Subject<Unit> _changed = new();
        private readonly Subject<SettingsCue> _cues = new();
        private Action[]? _revert;
        private CancellationTokenSource? _listen;
        private float _until;
        private int _shownSeconds;
        private bool _disposed;

        public SettingsMenu(SettingsLayout layout, ISettingsText text, Func<float> now, string? section = null,
            Action<string>? logWarning = null)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Text = text ?? throw new ArgumentNullException(nameof(text));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _logWarning = logWarning ?? Debug.LogWarning;
            int index = 0;
            for (int i = 0; i < Sections.Count; i++)
            {
                if (Sections[i].Id == section) index = i;
            }

            Open(index);
        }

        public ISettingsText Text { get; }

        public IReadOnlyList<SettingsSection> Sections => _layout.Sections;

        public int SectionIndex { get; private set; }

        public SettingsSection? Section => SectionIndex < Sections.Count ? Sections[SectionIndex] : null;

        /// <summary>The section's visible rows.</summary>
        public IReadOnlyList<SettingsRow> Rows => _rows;

        /// <summary>The focused row's index in <see cref="Rows"/>; -1 with nothing focusable.</summary>
        public int Focus { get; private set; } = -1;

        public SettingsRow? Focused => Focus >= 0 && Focus < _rows.Count ? _rows[Focus] : null;

        /// <summary>The question up; null while none is.</summary>
        public SettingsQuestion? Question { get; private set; }

        /// <summary>The question's text with its arguments (the seconds left first, for a timed one).</summary>
        public string QuestionText
        {
            get
            {
                if (Question == null) return "";
                string format = Text.Get(Question.TextKey);
                if (Question.Seconds <= 0f) return Question.Args.Length > 0 ? string.Format(format, Question.Args) : format;
                var args = new object[Question.Args.Length + 1];
                args[0] = QuestionSeconds;
                Array.Copy(Question.Args, 0, args, 1, Question.Args.Length);
                return string.Format(format, args);
            }
        }

        /// <summary>Whole seconds before a timed question says no by itself.</summary>
        public int QuestionSeconds => Question is { Seconds: > 0f } ? Mathf.Max(0, Mathf.CeilToInt(_until - _now())) : 0;

        /// <summary>The id of the row a key is listened for; null while none is.</summary>
        public string? Listening { get; private set; }

        private bool _rebuildAfterListening;

        /// <summary>The line under the rows: what the menu listens for, or the focused row's.</summary>
        public string Description
        {
            get
            {
                if (Listening != null)
                {
                    foreach (SettingsRow row in _all)
                    {
                        if (row.Id == Listening && row is KeysRow keys)
                        {
                            return string.Format(Text.Get(keys.ListenKey), Text.Get(keys.Id));
                        }
                    }
                }

                return Focused?.About(Text) ?? "";
            }
        }

        /// <summary>Anything shown changed.</summary>
        public Observable<Unit> Changed => _changed;

        public Observable<SettingsCue> Cues => _cues;

        private bool Busy => Listening != null || Question != null;

        public void Step(SettingsStep step)
        {
            if (_disposed || Listening != null) return;
            if (Question != null)
            {
                if (step is SettingsStep.Left or SettingsStep.Right)
                {
                    int choice = step == SettingsStep.Left ? 0 : 1;
                    if (choice != Question.Choice)
                    {
                        Question.Choice = choice;
                        Cue(SettingsCue.Focus);
                        Notify();
                    }
                }

                return;
            }

            switch (step)
            {
                case SettingsStep.Up:
                case SettingsStep.Down:
                    MoveFocus(step == SettingsStep.Up ? -1 : 1);
                    break;
                case SettingsStep.Left:
                case SettingsStep.Right:
                    if (Focused is { IsEnabled: true } row) Act(row, false, step == SettingsStep.Left ? -1 : 1);
                    break;
                case SettingsStep.PreviousSection:
                case SettingsStep.NextSection:
                    GoTo(SectionIndex + (step == SettingsStep.PreviousSection ? -1 : 1));
                    break;
            }
        }

        public void Submit()
        {
            if (_disposed || Listening != null) return;
            if (Question != null)
            {
                Answer(Question.Choice == 0);
                return;
            }

            SettingsRow? row = Focused;
            if (row == null) return;
            if (!row.IsEnabled)
            {
                Cue(SettingsCue.Denied);
                return;
            }

            if (row is KeysRow keys)
            {
                Listen(keys, keys.KeyFocus);
                return;
            }

            Act(row, true, 0);
        }

        /// <summary>Esc or B: true when the menu took it (a question answered no, a listening called off).</summary>
        public bool Back()
        {
            if (_disposed) return false;
            if (Listening != null)
            {
                _listen?.Cancel();
                return true;
            }

            if (Question != null)
            {
                Answer(false);
                return true;
            }

            return false;
        }

        /// <summary>Counts a timed question down; at zero it is answered no.</summary>
        public void Tick()
        {
            if (_disposed || Question is not { Seconds: > 0f }) return;
            if (_now() >= _until)
            {
                Answer(false);
                return;
            }

            int seconds = QuestionSeconds;
            if (seconds == _shownSeconds) return;
            _shownSeconds = seconds;
            Notify();
        }

        /// <summary>The pointer moved over a row: it takes the focus. Not for a row drawn under a pointer that stands still.</summary>
        public void Point(int row)
        {
            if (_disposed || Busy || row == Focus || !Focusable(row)) return;
            FocusOn(row);
            Cue(SettingsCue.Focus);
            Notify();
        }

        /// <summary>A click on a row: the focus and Enter.</summary>
        public void Click(int row)
        {
            if (_disposed || Busy || row < 0 || row >= _rows.Count) return;
            if (!_rows[row].Focusable)
            {
                if (_rows[row] is not HeadingRow) Cue(SettingsCue.Denied);
                return;
            }

            FocusOn(row);
            Submit();
            Notify();
        }

        /// <summary>A choice's arrow: -1 left, +1 right, 0 the next option round.</summary>
        public void Nudge(int row, int direction)
        {
            if (_disposed || Busy || !Focusable(row)) return;
            FocusOn(row);
            if (direction == 0) Submit();
            else Act(_rows[row], false, Math.Sign(direction));
            Notify();
        }

        /// <summary>A click on a meter's step: it lights up to it; on the last lit one it goes off, so the first step reaches 0.</summary>
        public void SetStep(int row, int step)
        {
            if (_disposed || Busy || !Focusable(row) || _rows[row] is not StepsRow steps) return;
            FocusOn(row);
            int target = Math.Clamp(step == steps.Value ? step - 1 : step, steps.Min, steps.Max);
            if (target != steps.Value) Act(steps, false, target - steps.Value);
            Notify();
        }

        /// <summary>A click on one key of a keys row: it listens for that key.</summary>
        public void ClickKey(int row, int key)
        {
            if (_disposed || Busy || !Focusable(row) || _rows[row] is not KeysRow keys) return;
            FocusOn(row);
            Listen(keys, key);
        }

        public void ClickSection(int section)
        {
            if (_disposed || Busy) return;
            GoTo(section);
        }

        /// <summary>A click on an answer.</summary>
        public void Answer(bool yes)
        {
            if (_disposed || Question == null) return;
            SettingsQuestion question = Question;
            Question = null;
            question.Answered(yes);
            Cue(yes ? SettingsCue.Select : SettingsCue.Back);
            Notify();
        }

        /// <summary>The pointer moved over an answer: it is the picked one.</summary>
        public void PointAnswer(bool yes)
        {
            int choice = yes ? 0 : 1;
            if (_disposed || Question == null || Question.Choice == choice) return;
            Question.Choice = choice;
            Cue(SettingsCue.Focus);
            Notify();
        }

        public void Ask(SettingsQuestion question)
        {
            if (_disposed) return;
            Question = question ?? throw new ArgumentNullException(nameof(question));
            if (question.Seconds > 0f)
            {
                _until = _now() + question.Seconds;
                _shownSeconds = QuestionSeconds;
            }

            Notify();
        }

        public void Cue(SettingsCue cue)
        {
            if (!_disposed) _cues.OnNext(cue);
        }

        /// <summary>
        /// Which rows are visible and on, asked again, the focus kept by id. The screen calls it on whatever those read
        /// (a setting's change, the device in hand).
        /// </summary>
        public void Refresh()
        {
            if (_disposed) return;
            string? focused = Focused?.Id;
            int was = Focus;
            _rows.Clear();
            foreach (SettingsRow row in _all)
            {
                if (row.IsVisible) _rows.Add(row);
            }

            Focus = -1;
            for (int i = 0; i < _rows.Count && focused != null; i++)
            {
                if (_rows[i].Id == focused && _rows[i].Focusable) Focus = i;
            }

            if (Focus < 0) Focus = Nearest(Math.Max(0, was));
            Notify();
        }

        public void Rebuild()
        {
            if (_disposed || Section == null) return;
            if (Listening != null)
            {
                // The rows stay as they are until the key comes; the listened row must not go from under it.
                _rebuildAfterListening = true;
                return;
            }

            var keyFocus = new Dictionary<string, int>();
            foreach (SettingsRow row in _all)
            {
                if (row is KeysRow keys) keyFocus[row.Id] = keys.KeyFocus;
            }

            Section.MakeRows(_all);
            Bind();
            foreach (SettingsRow row in _all)
            {
                row.Open();
                if (row is KeysRow keys && keyFocus.TryGetValue(row.Id, out int key))
                {
                    keys.KeyFocus = Math.Clamp(key, 0, Math.Max(0, keys.Count - 1));
                }
            }

            Refresh();
        }

        /// <summary>An unanswered question is answered no (a new window goes back); a listening is called off.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _listen?.Cancel();
            _listen?.Dispose();
            _listen = null;
            SettingsQuestion? question = Question;
            Question = null;
            question?.Answered(false);
            _changed.Dispose();
            _cues.Dispose();
        }

        internal void AskReset()
        {
            Ask(new SettingsQuestion("settings.reset.question", "settings.yes", "settings.no", yes =>
            {
                if (yes) ResetSection();
            }, startOnNo: true));
        }

        /// <summary>APPLY: the section's held picks go on together, then one keep question; false with nothing held.</summary>
        internal bool ApplyHeld()
        {
            var revert = new List<Action>();
            foreach (SettingsRow row in _all)
            {
                if (!row.IsHeld) continue;
                Action? back = row.Apply();
                if (back != null) revert.Add(back);
            }

            if (revert.Count == 0) return false;
            Cue(SettingsCue.Change);
            _revert = revert.ToArray();
            Ask(new SettingsQuestion("settings.keep.question", "settings.keep", "settings.revert", Kept,
                seconds: KeepSeconds));
            return true;
        }

        // The APPLY rows see the section's rows.
        private void Bind()
        {
            foreach (SettingsRow row in _all)
            {
                if (row is ApplyRow apply) apply.SectionRows = _all;
            }
        }

        private void ResetSection()
        {
            SettingsSection? section = Section;
            if (section == null) return;
            // A reset puts values on at once: held picks go, and the holding rows ask like any keep-or-revert row.
            foreach (SettingsRow row in _all) row.Drop();
            BeforeChange(true);
            // A reset may make the rows again (keys put back): go over the rows as they were.
            foreach (SettingsRow row in _all.ToArray())
            {
                foreach (IResettable value in row.Resettables) value.Reset();
            }

            section.OnReset?.Invoke();
            AfterChange();
            Refresh();
        }

        private void Open(int section)
        {
            SectionIndex = Mathf.Clamp(section, 0, Math.Max(0, Sections.Count - 1));
            _all.Clear();
            Section?.MakeRows(_all);
            Bind();
            foreach (SettingsRow row in _all) row.Open();
            _rows.Clear();
            foreach (SettingsRow row in _all)
            {
                if (row.IsVisible) _rows.Add(row);
            }

            Focus = Nearest(0);
            if (Focused is KeysRow keys) keys.KeyFocus = 0;
        }

        private void GoTo(int section)
        {
            section = Mathf.Clamp(section, 0, Math.Max(0, Sections.Count - 1));
            if (section == SectionIndex) return;
            Open(section);
            Cue(SettingsCue.Section);
            Notify();
        }

        private void MoveFocus(int direction)
        {
            for (int i = Focus + direction; i >= 0 && i < _rows.Count; i += direction)
            {
                if (!_rows[i].Focusable) continue;
                FocusOn(i);
                Cue(SettingsCue.Focus);
                Notify();
                return;
            }
        }

        private void FocusOn(int row)
        {
            if (row == Focus) return;
            Focus = row;
            if (_rows[row] is KeysRow keys) keys.KeyFocus = 0;
        }

        private bool Focusable(int row) => row >= 0 && row < _rows.Count && _rows[row].Focusable;

        // The first focusable row from start on, else the last before it.
        private int Nearest(int start)
        {
            for (int i = start; i < _rows.Count; i++)
            {
                if (_rows[i].Focusable) return i;
            }

            for (int i = Math.Min(start, _rows.Count) - 1; i >= 0; i--)
            {
                if (_rows[i].Focusable) return i;
            }

            return -1;
        }

        // A step or Enter on a row, with its cue and keep-or-revert.
        private void Act(SettingsRow row, bool submit, int direction)
        {
            BeforeChange();
            object? before = row.Current;
            bool shown = submit ? row.Submit(this) : row.Step(direction, this);
            bool changed = !Equals(before, row.Current);
            if (changed) Cue(SettingsCue.Change);
            else if (shown) Cue(submit ? SettingsCue.Select : SettingsCue.Focus);
            AfterChange();
            if (shown || changed) Notify();
        }

        // What the section's keep-or-revert rows show before a change, and what puts it back unless a keep question
        // already holds the first. Rows that hold their picks ask on APPLY instead, unless a reset changes them.
        private void BeforeChange(bool holding = false)
        {
            _keepRows.Clear();
            _keepBefore.Clear();
            _keepRestore.Clear();
            foreach (SettingsRow row in _all)
            {
                if (!row.IsKeepOrRevert || (row.Holds && !holding)) continue;
                _keepRows.Add(row);
                _keepBefore.Add(row.Current);
                _keepRestore.Add(_revert == null ? row.Remember() : null);
            }
        }

        // A keep-or-revert row changed: ask to keep it; a second change while asked only extends the time.
        private void AfterChange()
        {
            bool changed = false;
            for (int i = 0; i < _keepRows.Count; i++)
            {
                if (!Equals(_keepBefore[i], _keepRows[i].Current)) changed = true;
            }

            if (!changed) return;
            if (_revert != null && Question is { Seconds: > 0f })
            {
                _until = _now() + KeepSeconds;
                _shownSeconds = QuestionSeconds;
                return;
            }

            var revert = new List<Action>();
            foreach (Action? restore in _keepRestore)
            {
                if (restore != null) revert.Add(restore);
            }

            _revert = revert.ToArray();
            Ask(new SettingsQuestion("settings.keep.question", "settings.keep", "settings.revert", Kept,
                seconds: KeepSeconds));
        }

        private void Kept(bool yes)
        {
            Action[]? revert = _revert;
            _revert = null;
            if (yes || revert == null) return;
            foreach (Action restore in revert) restore();
        }

        private void Listen(KeysRow row, int key)
        {
            if (row.Count == 0) return;
            row.KeyFocus = Math.Clamp(key, 0, row.Count - 1);
            Listening = row.Id;
            _listen?.Dispose();
            _listen = new CancellationTokenSource();
            Cue(SettingsCue.Select);
            Notify();
            ListenAsync(row, row.KeyFocus, _listen.Token).Forget();
        }

        private async UniTaskVoid ListenAsync(KeysRow row, int key, CancellationToken ct)
        {
            try
            {
                await row.ListenAsync(key, this, ct);
            }
            catch (OperationCanceledException)
            {
                Cue(SettingsCue.Back);
            }
            catch (Exception e)
            {
                // A wait the console already holds, a map that is on: the menu must not stay listening.
                _logWarning($"Settings: {row.Id} could not wait for a key: {e.Message}");
                Cue(SettingsCue.Denied);
            }

            if (_disposed) return;
            Listening = null;
            if (_rebuildAfterListening)
            {
                _rebuildAfterListening = false;
                Rebuild();
            }

            Notify();
        }

        private void Notify()
        {
            if (!_disposed) _changed.OnNext(Unit.Default);
        }
    }
}
