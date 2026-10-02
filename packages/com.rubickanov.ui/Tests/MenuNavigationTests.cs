using System.Collections.Generic;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace Rubickanov.UI.Tests
{
    [TestFixture]
    public class MenuNavigationTests
    {
        private MenuNavigation _navigation = null!;
        private float _now;

        [SetUp]
        public void SetUp()
        {
            _navigation = new MenuNavigation();
            _now = 10f;
        }

        [TearDown]
        public void TearDown() => _navigation.Dispose();

        private void Frame(MenuInput input, float seconds = 0.02f)
        {
            _now += seconds;
            _navigation.Update(input, _now);
        }

        private void Tap(MenuInput input)
        {
            Frame(input);
            Frame(default);
        }

        private static MenuInput Navigate(float x, float y) => new() { Navigate = new Vector2(x, y) };

        [Test]
        public void Update_DirectionThenSubmit_StepsAndPressesTheClaimer()
        {
            var menu = new Target();
            using (_navigation.Claim(menu))
            {
                Tap(Navigate(0f, -1f));
                Tap(new MenuInput { Submit = true });
            }

            CollectionAssert.AreEqual(new[] { MenuStep.Down }, menu.Steps);
            Assert.AreEqual(1, menu.Submits);
        }

        [Test]
        public void Update_TwoClaims_LastHearsUntilItGoes()
        {
            var under = new Target();
            var over = new Target();
            using (_navigation.Claim(under))
            {
                using (_navigation.Claim(over))
                {
                    Tap(Navigate(0f, -1f));
                }

                Tap(Navigate(0f, 1f));
            }

            CollectionAssert.AreEqual(new[] { MenuStep.Down }, over.Steps);
            CollectionAssert.AreEqual(new[] { MenuStep.Up }, under.Steps);
            Assert.IsNull(_navigation.Current);
        }

        [Test]
        public void Update_UnderClaimGoesFirst_OverKeepsHearing()
        {
            var under = new Target();
            var over = new Target();
            var underClaim = _navigation.Claim(under);
            using var overClaim = _navigation.Claim(over);

            underClaim.Dispose();
            Tap(Navigate(0f, -1f));

            CollectionAssert.AreEqual(new[] { MenuStep.Down }, over.Steps);
            Assert.AreSame(over, _navigation.Current);
        }

        [Test]
        public void Claim_DirectionHeldIntoTheMenu_WaitsForItsRelease()
        {
            var menu = new Target();
            Frame(Navigate(0f, 1f));
            using (_navigation.Claim(menu))
            {
                Frame(Navigate(0f, 1f), 1f);
                Frame(default);
                Tap(Navigate(0f, 1f));
            }

            CollectionAssert.AreEqual(new[] { MenuStep.Up }, menu.Steps, "only the press after the release");
        }

        [Test]
        public void WaitForRelease_InputOffWhileHeld_HeldKeyDoesNotStepWhenBack()
        {
            var menu = new Target();
            using var claim = _navigation.Claim(menu);
            Frame(default);
            Frame(Navigate(1f, 0f));

            _navigation.WaitForRelease();
            Frame(Navigate(1f, 0f), 1f);
            Frame(Navigate(0f, -1f));

            CollectionAssert.AreEqual(new[] { MenuStep.Right, MenuStep.Down }, menu.Steps,
                "the held key waits for its release, another key steps");
        }

        [Test]
        public void Update_DirectionHeld_RepeatsAfterTheDelay()
        {
            var menu = new Target();
            using var claim = _navigation.Claim(menu);
            Frame(default);

            for (var i = 0; i < 75; i++) Frame(Navigate(0f, -1f), 0.01f);

            Assert.AreEqual(5, menu.Steps.Count, "at once, then at 0.4, 0.5, 0.6 and 0.7 s within 0.75 s");
        }

        [Test]
        public void Update_PageAxis_StepsAPageAndRepeats()
        {
            var menu = new Target();
            using var claim = _navigation.Claim(menu);
            Frame(default);

            Frame(new MenuInput { Page = 1f });
            Frame(new MenuInput { Page = 1f }, 0.5f);
            Tap(new MenuInput { Page = -1f });

            CollectionAssert.AreEqual(new[] { MenuStep.NextPage, MenuStep.NextPage, MenuStep.PreviousPage }, menu.Steps);
        }

        [Test]
        public void Update_TabPress_StepsATabEachWay()
        {
            var menu = new Target();
            using var claim = _navigation.Claim(menu);

            Tap(new MenuInput { Tab = 1 });
            Tap(new MenuInput { Tab = -1 });

            CollectionAssert.AreEqual(new[] { MenuStep.NextTab, MenuStep.PreviousTab }, menu.Steps);
        }

        [Test]
        public void Update_SubmitOpensAMenuOver_NewMenuDoesNotGetThatFramesSteps()
        {
            var over = new Target();
            System.IDisposable? overClaim = null;
            var under = new Target { OnSubmit = () => overClaim = _navigation.Claim(over) };
            using var claim = _navigation.Claim(under);

            Frame(new MenuInput { Submit = true, Tab = 1 });
            overClaim?.Dispose();

            Assert.AreEqual(1, under.Submits);
            CollectionAssert.IsEmpty(over.Steps);
        }

        [Test]
        public void Update_NoClaim_NothingThrows()
        {
            Assert.DoesNotThrow(() => Tap(new MenuInput { Navigate = Vector2.down, Submit = true, Tab = 1 }));
        }

        [Test]
        public void Cue_Subscribed_ReceivesCuesInOrder()
        {
            var cues = new List<MenuCue>();
            var tape = new MenuCue("tape");
            using var subscription = _navigation.Cues.Subscribe(cues.Add);

            _navigation.Cue(MenuCue.Focus);
            _navigation.Cue(tape);

            CollectionAssert.AreEqual(new[] { MenuCue.Focus, tape }, cues);
        }

        [Test]
        public void Point_Subscribed_ReportsThePointer()
        {
            var pointed = 0;
            using var subscription = _navigation.Pointed.Subscribe(_ => pointed++);

            _navigation.Point();

            Assert.AreEqual(1, pointed);
        }

        // ── StepRepeat ───────────────────────────────────────────

        [Test]
        public void StepRepeat_HeldDirection_StepsAtOnceThenRepeats()
        {
            var repeat = new StepRepeat();
            var steps = new List<float>();
            for (var t = 0f; t < 0.75f; t += 0.01f)
            {
                if (repeat.Next(MenuStep.Down, t, out _)) steps.Add(t);
            }

            Assert.AreEqual(0f, steps[0]);
            Assert.AreEqual(repeat.Delay, steps[1], 0.011f);
            Assert.AreEqual(repeat.Interval, steps[2] - steps[1], 0.011f);
            Assert.AreEqual(5, steps.Count);
        }

        [Test]
        public void StepRepeat_NewDirectionOrRelease_IsANewPress()
        {
            var repeat = new StepRepeat();

            Assert.IsTrue(repeat.Next(MenuStep.Down, 0f, out _));
            Assert.IsFalse(repeat.Next(MenuStep.Down, 0.1f, out _));
            Assert.IsTrue(repeat.Next(MenuStep.Up, 0.15f, out var step));
            Assert.AreEqual(MenuStep.Up, step);
            Assert.IsFalse(repeat.Next(null, 0.2f, out _));
            Assert.IsTrue(repeat.Next(MenuStep.Up, 0.25f, out _));
        }

        [Test]
        public void StepRepeat_CustomTimings_Used()
        {
            var repeat = new StepRepeat(delay: 0.2f, interval: 0.05f);

            repeat.Next(MenuStep.Down, 0f, out _);

            Assert.IsFalse(repeat.Next(MenuStep.Down, 0.19f, out _));
            Assert.IsTrue(repeat.Next(MenuStep.Down, 0.2f, out _));
            Assert.IsTrue(repeat.Next(MenuStep.Down, 0.25f, out _));
        }

        [TestCase(0f, 0.3f, null)]
        [TestCase(0.2f, 0.9f, MenuStep.Up)]
        [TestCase(0.6f, -0.5f, MenuStep.Right)]
        [TestCase(0f, -1f, MenuStep.Down)]
        [TestCase(-1f, 0f, MenuStep.Left)]
        public void Direction_Stick_StepsAlongLongerAxisPastDeadZone(float x, float y, MenuStep? expected)
        {
            Assert.AreEqual(expected, StepRepeat.Direction(new Vector2(x, y)));
        }

        private sealed class Target : IMenuTarget
        {
            public readonly List<MenuStep> Steps = new();
            public int Submits;
            public System.Action? OnSubmit;

            public void Step(MenuStep step) => Steps.Add(step);

            public void Submit()
            {
                Submits++;
                OnSubmit?.Invoke();
            }
        }
    }
}
