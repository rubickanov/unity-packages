using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using Rubickanov.Save;

namespace Rubickanov.Settings.Tests
{
    /// <summary>The settings menu model: focus, steps, sections, questions, keep-or-revert, reset, listening, the pointer.</summary>
    public sealed class SettingsMenuTests
    {
        private SettingsStore _store = null!;
        private StepSetting _master = null!;
        private StepSetting _music = null!;
        private Setting<bool> _subtitles = null!;
        private Setting<WindowMode> _window = null!;
        private ChoiceSetting<int> _quality = null!;
        private bool _hiddenShown;
        private bool _offOn;
        private float _now;
        private List<SettingsCue> _cues = null!;
        private List<string> _warnings = null!;
        private SettingsMenu? _menu;

        [SetUp]
        public void SetUp()
        {
            _store = new SettingsStore(new MemorySaveStore(), new SaveKey(SaveArea.AtRoot("Settings", "settings"), "settings.json"),
                new SettingsFileOptions { SaveOnQuit = false, LogError = _ => { }, LogWarning = _ => { } });
            _master = _store.Steps("sound.master", 10);
            _music = _store.Steps("sound.music", 7);
            _subtitles = _store.Toggle("sound.subtitles", true);
            _window = _store.Enum("display.window", WindowMode.Fullscreen);
            _quality = _store.Choice("display.quality", 1, new[] { 0, 1, 2 }, SettingFormats.Int);
            _store.ReadAsync().GetAwaiter().GetResult();
            _hiddenShown = false;
            _offOn = false;
            _now = 0f;
            _cues = new List<SettingsCue>();
            _warnings = new List<string>();
        }

        [TearDown]
        public void TearDown()
        {
            _menu?.Dispose();
            _store.Dispose();
        }

        [Test]
        public void Step_UpAndDown_PassHeadingsOffAndHiddenRowsAndStopAtTheEnds()
        {
            SettingsMenu menu = Menu(Sound);

            Assert.That(menu.Focused!.Id, Is.EqualTo("sound.master"));
            menu.Step(SettingsStep.Down);
            Assert.That(menu.Focused!.Id, Is.EqualTo("sound.music"), "past the heading, the hidden and the off rows");
            menu.Step(SettingsStep.Down);
            menu.Step(SettingsStep.Down);
            menu.Step(SettingsStep.Down);
            Assert.That(menu.Focused!.Id, Is.EqualTo(SectionBuilder.ResetId));
            menu.Step(SettingsStep.Down);
            Assert.That(menu.Focused!.Id, Is.EqualTo(SectionBuilder.ResetId), "no going round");
            menu.Step(SettingsStep.Up);
            menu.Step(SettingsStep.Up);
            menu.Step(SettingsStep.Up);
            menu.Step(SettingsStep.Up);
            menu.Step(SettingsStep.Up);
            Assert.That(menu.Focused!.Id, Is.EqualTo("sound.master"));
        }

        [Test]
        public void Step_LeftAndRight_StepsStopAtTheEndsAndChoicesDoNotGoRound()
        {
            SettingsMenu menu = Menu(Sound);

            menu.Step(SettingsStep.Right);
            Assert.That(_master.Value, Is.EqualTo(10));
            menu.Step(SettingsStep.Left);
            Assert.That(_master.Value, Is.EqualTo(9));
            Assert.That(_cues, Is.EqualTo(new[] { SettingsCue.Change }));

            menu.ClickSection(1);
            menu.Step(SettingsStep.Down);
            menu.Step(SettingsStep.Right);
            menu.Step(SettingsStep.Right);
            Assert.That(_quality.Value, Is.EqualTo(2));
            menu.Step(SettingsStep.Right);
            Assert.That(_quality.Value, Is.EqualTo(2));
        }

        [Test]
        public void Submit_OnAChoiceAndAToggle_GoesRound()
        {
            SettingsMenu menu = Menu(Sound);
            menu.ClickSection(1);
            menu.Step(SettingsStep.Down);

            menu.Submit();
            menu.Submit();

            Assert.That(_quality.Value, Is.EqualTo(0));
            menu.ClickSection(0);
            menu.Click(IndexOf(menu, "sound.subtitles"));
            Assert.That(_subtitles.Value, Is.False);
        }

        [Test]
        public void Step_Sections_StopAtTheEndsWithTheirCue()
        {
            SettingsMenu menu = Menu(Sound);

            menu.Step(SettingsStep.PreviousSection);
            menu.Step(SettingsStep.NextSection);
            menu.Step(SettingsStep.NextSection);

            Assert.That(menu.SectionIndex, Is.EqualTo(1));
            Assert.That(menu.Section!.Id, Is.EqualTo("display"));
            Assert.That(_cues, Is.EqualTo(new[] { SettingsCue.Section }));
        }

        [Test]
        public void KeepOrRevert_AStepOnlyPicks_ApplyPutsItOnAndAsks()
        {
            SettingsMenu menu = Menu(Sound, "display");
            int apply = IndexOf(menu, SectionBuilder.ApplyId);
            Assert.That(menu.Rows[apply].IsEnabled, Is.False, "nothing to apply yet");

            menu.Step(SettingsStep.Right);
            Assert.That(menu.Question, Is.Null);
            Assert.That(_window.Value, Is.EqualTo(WindowMode.Fullscreen), "the screen waits for APPLY");
            Assert.That(menu.Rows[0].IsHeld, Is.True);
            Assert.That(((ChoiceRow)menu.Rows[0]).Index, Is.EqualTo(1), "the pick is shown");
            Assert.That(menu.Rows[apply].IsEnabled, Is.True);

            menu.Click(apply);
            Assert.That(_window.Value, Is.EqualTo(WindowMode.Windowed));
            Assert.That(menu.Question!.TextKey, Is.EqualTo("settings.keep.question"));
            Assert.That(menu.QuestionText, Does.Contain("10"));
            Assert.That(menu.Rows[0].IsHeld, Is.False);
        }

        [Test]
        public void KeepOrRevert_StepBackToTheValue_HoldsNothing()
        {
            SettingsMenu menu = Menu(Sound, "display");

            menu.Step(SettingsStep.Right);
            menu.Step(SettingsStep.Left);

            Assert.That(menu.Rows[0].IsHeld, Is.False);
            Assert.That(menu.Rows[IndexOf(menu, SectionBuilder.ApplyId)].IsEnabled, Is.False);
        }

        [Test]
        public void KeepOrRevert_Yes_KeepsTheNewValue()
        {
            SettingsMenu menu = Menu(Sound, "display");

            menu.Step(SettingsStep.Right);
            menu.Click(IndexOf(menu, SectionBuilder.ApplyId));
            menu.Submit();

            Assert.That(menu.Question, Is.Null);
            Assert.That(_window.Value, Is.EqualTo(WindowMode.Windowed));
        }

        [Test]
        public void KeepOrRevert_No_PutsTheOldValueBack()
        {
            SettingsMenu menu = Menu(Sound, "display");

            menu.Step(SettingsStep.Right);
            menu.Click(IndexOf(menu, SectionBuilder.ApplyId));
            menu.Step(SettingsStep.Right);
            menu.Submit();

            Assert.That(_window.Value, Is.EqualTo(WindowMode.Fullscreen));
            Assert.That(_cues[_cues.Count - 1], Is.EqualTo(SettingsCue.Back));
        }

        [Test]
        public void KeepOrRevert_Unanswered_GoesBackWhenTheSecondsRunOut()
        {
            SettingsMenu menu = Menu(Sound, "display");
            menu.Step(SettingsStep.Right);
            menu.Click(IndexOf(menu, SectionBuilder.ApplyId));

            _now = 3.5f;
            menu.Tick();
            Assert.That(menu.QuestionSeconds, Is.EqualTo(7));
            Assert.That(_window.Value, Is.EqualTo(WindowMode.Windowed));
            _now = 10f;
            menu.Tick();

            Assert.That(menu.Question, Is.Null);
            Assert.That(_window.Value, Is.EqualTo(WindowMode.Fullscreen));
        }

        [Test]
        public void KeepOrRevert_LeavingTheSection_DropsThePick()
        {
            SettingsMenu menu = Menu(Sound, "display");
            menu.Step(SettingsStep.Right);

            menu.Step(SettingsStep.PreviousSection);
            menu.Step(SettingsStep.NextSection);

            Assert.That(menu.Rows[0].IsHeld, Is.False);
            Assert.That(((ChoiceRow)menu.Rows[0]).Index, Is.EqualTo(0));
            Assert.That(_window.Value, Is.EqualTo(WindowMode.Fullscreen));
        }

        [Test]
        public void Dispose_WithTheKeepQuestionUp_PutsTheOldValueBack()
        {
            SettingsMenu menu = Menu(Sound, "display");
            menu.Step(SettingsStep.Right);
            menu.Click(IndexOf(menu, SectionBuilder.ApplyId));

            menu.Dispose();

            Assert.That(_window.Value, Is.EqualTo(WindowMode.Fullscreen));
        }

        [Test]
        public void ResetRow_AsksFirstThenResetsOnlyItsSectionAndItsOwnReset()
        {
            float voice = 0.2f;
            bool ownReset = false;
            var layout = new SettingsLayout()
                .Section("sound", rows =>
                {
                    rows.Steps(_master);
                    rows.Steps("voice.volume", new Bound<int>(() => (int)(voice * 10), v => voice = v / 10f, 10), 0, 10);
                    rows.ResetRow();
                }, () => ownReset = true)
                .Section("display", rows => rows.Choice(_quality, q => "q" + q));
            _master.Value = 2;
            _quality.Value = 2;
            SettingsMenu menu = Menu(layout);
            menu.Click(IndexOf(menu, SectionBuilder.ResetId));

            Assert.That(menu.Question!.Choice, Is.EqualTo(1), "the reset question starts on no");
            menu.Submit();
            Assert.That(_master.Value, Is.EqualTo(2));
            menu.Click(IndexOf(menu, SectionBuilder.ResetId));
            menu.Step(SettingsStep.Left);
            menu.Submit();

            Assert.That(_master.Value, Is.EqualTo(10));
            Assert.That(voice, Is.EqualTo(1f));
            Assert.That(ownReset, Is.True);
            Assert.That(_quality.Value, Is.EqualTo(2));
        }

        [Test]
        public void ResetRow_ChangingAKeepRow_AsksToKeepAndNoPutsItBack()
        {
            _window.Value = WindowMode.Windowed;
            SettingsMenu menu = Menu(Sound, "display");
            menu.Click(IndexOf(menu, SectionBuilder.ResetId));
            menu.Answer(true);

            Assert.That(_window.Value, Is.EqualTo(WindowMode.Fullscreen));
            Assert.That(menu.Question!.TextKey, Is.EqualTo("settings.keep.question"));
            menu.Answer(false);
            Assert.That(_window.Value, Is.EqualTo(WindowMode.Windowed));
        }

        [Test]
        public void Back_AnswersAQuestionNoElseLeavesTheMenuToItsOwner()
        {
            SettingsMenu menu = Menu(Sound, "display");
            menu.Step(SettingsStep.Right);
            menu.Click(IndexOf(menu, SectionBuilder.ApplyId));

            Assert.That(menu.Back(), Is.True);
            Assert.That(menu.Question, Is.Null);
            Assert.That(_window.Value, Is.EqualTo(WindowMode.Fullscreen));
            Assert.That(menu.Back(), Is.False);
        }

        [Test]
        public void SetStep_OnTheLastLitStep_TurnsItOff()
        {
            SettingsMenu menu = Menu(Sound);
            int row = IndexOf(menu, "sound.music");

            menu.SetStep(row, 3);
            Assert.That(_music.Value, Is.EqualTo(3));
            Assert.That(menu.Focused!.Id, Is.EqualTo("sound.music"));
            menu.SetStep(row, 1);
            menu.SetStep(row, 1);

            Assert.That(_music.Value, Is.EqualTo(0));
        }

        [Test]
        public void Refresh_ARowAppearsAbove_TheFocusStaysOnItsRow()
        {
            SettingsMenu menu = Menu(Sound);
            menu.Step(SettingsStep.Down);
            int changes = 0;
            using IDisposable counting = menu.Changed.Subscribe(_ => changes++);

            _hiddenShown = true;
            menu.Refresh();

            Assert.That(menu.Focused!.Id, Is.EqualTo("sound.music"));
            Assert.That(IndexOf(menu, "sound.hidden"), Is.GreaterThanOrEqualTo(0));
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void Step_AGamesOwnKindOfRow_TakesStepsAndEnter()
        {
            var lap = new LapRow("race.laps");
            SettingsMenu menu = Menu(new SettingsLayout().Section("race", rows => rows.Add(lap)));

            menu.Step(SettingsStep.Right);
            menu.Step(SettingsStep.Right);
            menu.Submit();

            Assert.That(lap.Laps, Is.EqualTo(1));
            Assert.That(_cues, Is.EqualTo(new[] { SettingsCue.Change, SettingsCue.Change, SettingsCue.Change }));
        }

        [Test]
        public void Submit_OnAKeysRow_ListensUntilTheRowIsDone()
        {
            var keys = new FakeKeys("keys.jump");
            SettingsMenu menu = Menu(new SettingsLayout().Section("controls", rows => rows.Add(keys)));
            menu.Step(SettingsStep.Right);

            menu.Submit();
            Assert.That(menu.Listening, Is.EqualTo("keys.jump"));
            Assert.That(keys.Asked, Is.EqualTo(1));
            Assert.That(menu.Description, Does.Contain("keys.jump"));
            menu.Step(SettingsStep.Left);
            Assert.That(keys.KeyFocus, Is.EqualTo(1), "no steps while listening");
            keys.Waiting!.TrySetResult();

            Assert.That(menu.Listening, Is.Null);
            Assert.That(_cues, Is.EqualTo(new[] { SettingsCue.Focus, SettingsCue.Select }));
        }

        [Test]
        public void Back_WhileListening_CallsTheListeningOff()
        {
            var keys = new FakeKeys("keys.jump");
            SettingsMenu menu = Menu(new SettingsLayout().Section("controls", rows => rows.Add(keys)));
            menu.ClickKey(0, 1);

            Assert.That(menu.Back(), Is.True);

            Assert.That(menu.Listening, Is.Null);
            Assert.That(_cues[_cues.Count - 1], Is.EqualTo(SettingsCue.Back));
            Assert.That(menu.Back(), Is.False);
        }

        [Test]
        public void Submit_AListeningThatBreaks_LeavesTheMenuAlive()
        {
            var keys = new FakeKeys("keys.jump") { Throw = new InvalidOperationException("the console waits") };
            SettingsMenu menu = Menu(new SettingsLayout().Section("controls", rows => rows.Add(keys)));

            menu.Submit();

            Assert.That(menu.Listening, Is.Null);
            Assert.That(_cues[_cues.Count - 1], Is.EqualTo(SettingsCue.Denied));
            Assert.That(_warnings, Has.Count.EqualTo(1));
        }

        [Test]
        public void SizeRow_NativeShowsAsNowAndARevertPutsNativeBack()
        {
            var display = new DisplaySettings(_store, "screen");
            var modes = new Modes();
            SettingsMenu menu = Menu(new SettingsLayout().Section("display", rows => display.AddRows(rows, modes)));
            var size = (ChoiceRow)menu.Rows[IndexOf(menu, "screen.size")];

            Assert.That(size.Index, Is.EqualTo(1));
            menu.Nudge(IndexOf(menu, "screen.size"), -1);
            Assert.That(display.Size.Value.IsNative, Is.True, "picked, not on yet");
            menu.Click(IndexOf(menu, SectionBuilder.ApplyId));
            Assert.That(display.Size.Value, Is.EqualTo(new ScreenSize(1280, 720)));
            menu.Answer(false);

            Assert.That(display.Size.Value.IsNative, Is.True);
            Assert.That(menu.Rows[IndexOf(menu, "screen.frameCap")].IsEnabled, Is.False, "no cap under vsync");
        }

        private void Sound(SectionBuilder rows)
        {
            rows.Steps(_master);
            rows.Heading("sound.heading");
            rows.Steps("sound.hidden", new Bound<int>(() => 1, _ => { }), 0, 10).Visible(() => _hiddenShown);
            rows.Toggle("sound.off", new Bound<bool>(() => true, _ => { })).Enabled(() => _offOn);
            rows.Steps(_music);
            rows.Toggle(_subtitles);
            rows.Action("sound.test", () => { });
            rows.ResetRow();
        }

        private SettingsMenu Menu(Action<SectionBuilder> sound, string? section = null) => Menu(new SettingsLayout()
            .Section("sound", sound)
            .Section("display", rows =>
            {
                rows.Choice(_window.Id, _window, new[] { WindowMode.Fullscreen, WindowMode.Windowed }, w => "display.window/" + w).KeepOrRevert();
                rows.ApplyRow();
                rows.Choice(_quality, q => "display.quality/" + q);
                rows.ResetRow();
            }), section);

        private SettingsMenu Menu(SettingsLayout layout, string? section = null)
        {
            _menu = new SettingsMenu(layout, SettingsText.English(_ => { }), () => _now, section, _warnings.Add);
            _menu.Cues.Subscribe(_cues.Add);
            return _menu;
        }

        private static int IndexOf(SettingsMenu menu, string id)
        {
            for (int i = 0; i < menu.Rows.Count; i++)
            {
                if (menu.Rows[i].Id == id) return i;
            }

            return -1;
        }

        private sealed class LapRow : SettingsRow
        {
            public LapRow(string id) : base(id)
            {
            }

            public int Laps { get; private set; } = 3;

            protected internal override object? Current => Laps;

            protected internal override bool Step(int direction, ISettingsMenu menu)
            {
                Laps = Math.Max(1, Laps + direction);
                return true;
            }

            protected internal override bool Submit(ISettingsMenu menu)
            {
                Laps = 1;
                return true;
            }
        }

        private sealed class FakeKeys : KeysRow
        {
            public FakeKeys(string id) : base(id)
            {
            }

            public UniTaskCompletionSource? Waiting { get; private set; }

            public Exception? Throw { get; set; }

            public int Asked { get; private set; }

            public override int Count => 2;

            public override string KeyText(int key) => key == 0 ? "Space" : "Pad A";

            public override bool IsDefault(int key) => true;

            protected internal override async UniTask ListenAsync(int key, ISettingsMenu menu, CancellationToken ct)
            {
                Asked++;
                if (Throw != null) throw Throw;
                Waiting = new UniTaskCompletionSource();
                using (ct.Register(() => Waiting.TrySetCanceled()))
                {
                    await Waiting.Task;
                }
            }
        }

        private sealed class Modes : IDisplayModes
        {
            public IReadOnlyList<ScreenSize> Sizes { get; } = new[] { new ScreenSize(1280, 720), new ScreenSize(1920, 1080) };

            public ScreenSize Current => new(1920, 1080);

            public ScreenSize Desktop => new(1920, 1080);
        }
    }
}
