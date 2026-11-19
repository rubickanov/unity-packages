using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.App.Tests
{
    public sealed class LifecycleTests
    {
        private readonly List<string> _calls = new();
        private Lifecycle _life;

        [SetUp]
        public void SetUp()
        {
            _calls.Clear();
            _life = new Lifecycle();
        }

        [TearDown]
        public void TearDown() => _life.Dispose();

        [Test]
        public void Initialize_SeveralSteps_RunAtOnceInOrder()
        {
            _life.Initialize(Call("a"), Call("b"));

            Assert.That(_calls, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void Start_TwoFrames_RunsOnceAtTheFirst()
        {
            _life.Start(Call("a"), Call("b"));
            Assert.That(_calls, Is.Empty, "Nothing starts before the frame.");

            _life.RunStarts();
            _life.RunStarts();

            Assert.That(_calls, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void Ticks_EveryPlace_RunEveryFrameInOrderGiven()
        {
            _life.Tick(Call("a"), Call("b"));
            _life.Tick(Call("c"));
            _life.LateTick(Call("late"));

            _life.RunTicks();
            _life.RunLateTicks();
            _life.RunTicks();

            Assert.That(_calls, Is.EqualTo(new[] { "a", "b", "c", "late", "a", "b", "c" }));
        }

        [Test]
        public void FrameLoop_AFrame_RunsThePlacesInFrameOrder()
        {
            _life.LateTick(Call("late"));
            _life.PreLateTick(Call("preLate"));
            _life.PhysicsTick(Call("physics"));
            _life.PrePhysicsTick(Call("prePhysics"));
            _life.FixedTick(Call("fixed"));
            _life.Tick(Call("tick"));
            _life.Start(Call("start"));
            _life.EarlyTick(Call("early"));
            _life.TimeTick(Call("time"));

            RunFrame();

            Assert.That(_calls, Is.EqualTo(new[]
            {
                "time", "early", "start", "tick", "fixed", "prePhysics", "physics", "preLate", "late"
            }));
        }

        [Test]
        public void FrameLoop_TwoScopes_RunTheFirstJoinedFirstAndEarlyTicksBeforeAnyStart()
        {
            using var scene = new Lifecycle();
            _life.EarlyTick(Call("app early"));
            _life.Start(Call("app start"));
            _life.Tick(Call("app tick"));
            scene.EarlyTick(Call("scene early"));
            scene.Start(Call("scene start"));
            scene.Tick(Call("scene tick"));

            FrameLoop.RunStarts();
            FrameLoop.RunTicks();

            Assert.That(_calls, Is.EqualTo(new[]
            {
                "app early", "scene early", "app start", "scene start", "app tick", "scene tick"
            }));
        }

        [Test]
        public void FrameLoop_ScopeDisposed_LeavesTheFrame()
        {
            var scene = new Lifecycle();
            scene.Tick(Call("scene"));

            scene.Dispose();
            FrameLoop.RunTicks();

            Assert.That(_calls, Is.Empty);
        }

        [Test]
        public void Dispose_StepsGiven_NothingRunsAfter()
        {
            _life.Start(Call("start"));
            _life.EarlyTick(Call("early"));
            _life.Tick(Call("tick"));
            _life.PreLateTick(Call("preLate"));
            _life.LateTick(Call("late"));

            _life.Dispose();
            RunFrame();

            Assert.That(_calls, Is.Empty);
        }

        [Test]
        public void Dispose_FromATick_StopsTheRestOfThatTick()
        {
            _life.Tick(Call("a"), () => _life.Dispose(), Call("b"));

            _life.RunTicks();

            Assert.That(_calls, Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void Tick_StepThrows_LoggedOnceAndTheOthersRunEveryFrame()
        {
            _life.Tick(Call("a"), () => throw new InvalidOperationException("broken tick"), Call("b"));

            LogAssert.Expect(LogType.Exception, new Regex("broken tick"));
            _life.RunTicks();
            _life.RunTicks();

            // A second exception logged would fail the test as unexpected.
            Assert.That(_calls, Is.EqualTo(new[] { "a", "b", "a", "b" }));
        }

        [Test]
        public void Tick_StepThrowsAnotherKind_LoggedAgain()
        {
            var round = 0;
            _life.Tick(() =>
            {
                round++;
                if (round == 1)
                {
                    throw new InvalidOperationException("first kind");
                }

                throw new ArgumentException("second kind");
            });

            LogAssert.Expect(LogType.Exception, new Regex("first kind"));
            LogAssert.Expect(LogType.Exception, new Regex("second kind"));
            _life.RunTicks();
            _life.RunTicks();
            _life.RunTicks();

            Assert.That(round, Is.EqualTo(3));
        }

        [Test]
        public void Start_StepThrows_LoggedAndTheOthersRun()
        {
            _life.Start(Call("a"), () => throw new InvalidOperationException("broken start"), Call("b"));

            LogAssert.Expect(LogType.Exception, new Regex("broken start"));
            _life.RunStarts();

            Assert.That(_calls, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void Dispose_Twice_OwnedServicesGoOnceLastTakenFirst()
        {
            _life.Own(new Recorded(_calls, "first"));
            _life.Own(new Recorded(_calls, "second"));
            _life.Own(new Recorded(_calls, "third"));

            _life.Dispose();
            _life.Dispose();

            Assert.That(_calls, Is.EqualTo(new[] { "third", "second", "first" }));
            Assert.That(_life.Disposed, Is.True);
        }

        [Test]
        public void Dispose_OwnedThrows_TheOthersStillGo()
        {
            _life.Own(new Recorded(_calls, "first"));
            _life.Own(new Recorded(_calls, "broken", true));
            _life.Own(new Recorded(_calls, "third"));

            LogAssert.Expect(LogType.Exception, new Regex("broken"));
            _life.Dispose();

            Assert.That(_calls, Is.EqualTo(new[] { "third", "broken", "first" }));
        }

        [Test]
        public void NameOf_MethodGroupAndLambda_NamedByTypeAndMethod()
        {
            Assert.That(Lifecycle.NameOf(Marked), Is.EqualTo(FrameLoop.MarkerPrefix + nameof(LifecycleTests) + ".Marked"));
            Assert.That(Lifecycle.NameOf(() => _calls.Clear()),
                Is.EqualTo(FrameLoop.MarkerPrefix + nameof(LifecycleTests) + "." + nameof(NameOf_MethodGroupAndLambda_NamedByTypeAndMethod) + "~"));
        }

        private static void RunFrame()
        {
            FrameLoop.RunTimeTicks();
            FrameLoop.RunStarts();
            FrameLoop.RunTicks();
            FrameLoop.RunFixedTicks();
            FrameLoop.RunPrePhysicsTicks();
            FrameLoop.RunPhysicsTicks();
            FrameLoop.RunPreLateTicks();
            FrameLoop.RunLateTicks();
        }

        private static void Marked()
        {
        }

        private Action Call(string name) => () => _calls.Add(name);

        private sealed class Recorded : IDisposable
        {
            private readonly List<string> _calls;
            private readonly string _name;
            private readonly bool _throws;

            public Recorded(List<string> calls, string name, bool throws = false)
            {
                _calls = calls;
                _name = name;
                _throws = throws;
            }

            public void Dispose()
            {
                _calls.Add(_name);
                if (_throws)
                {
                    throw new InvalidOperationException(_name);
                }
            }
        }
    }
}
