using NUnit.Framework;
using Rubickanov.ACS.Runtime;
using UnityEngine;

namespace Rubickanov.ACS.Tests
{
    /// <summary>
    /// Exercises <see cref="EntityTickRunner"/>. Uses a real
    /// <c>GameObject</c> so the <c>Update</c> path runs as it would in play
    /// mode, but drives the tick by invoking <c>Update</c> directly via
    /// reflection — the player loop doesn't run in edit-mode tests.
    /// </summary>
    [TestFixture]
    public class EntityTickRunnerTests
    {
        private GameObject _gameObject = default!;
        private EntityTickRunner _runner = default!;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject(nameof(EntityTickRunnerTests));
            _runner = _gameObject.AddComponent<EntityTickRunner>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void Register_TickablesReceiveDelta()
        {
            var a = new RecordingTickable();
            var b = new RecordingTickable();
            _runner.Register(a);
            _runner.Register(b);

            InvokeUpdate();

            Assert.AreEqual(1, a.TickCount);
            Assert.AreEqual(1, b.TickCount);
        }

        [Test]
        public void Register_SameTickableTwice_InvokedOncePerFrame()
        {
            // Double-registration could create a duplicate tick in one frame.
            // The runner guards against that so callers don't need an
            // "already-added" flag.
            var a = new RecordingTickable();
            _runner.Register(a);
            _runner.Register(a);

            InvokeUpdate();

            Assert.AreEqual(1, a.TickCount);
        }

        [Test]
        public void Unregister_StopsFurtherTicks()
        {
            var a = new RecordingTickable();
            _runner.Register(a);
            InvokeUpdate();
            Assert.AreEqual(1, a.TickCount);

            _runner.Unregister(a);
            InvokeUpdate();

            Assert.AreEqual(1, a.TickCount);
        }

        [Test]
        public void Tickable_UnregisteringDuringOwnTick_DoesNotBreakSiblings()
        {
            // A tickable that removes itself mid-tick must not corrupt iteration
            // for sibling tickables that still need this frame's update.
            var sibling = new RecordingTickable();
            SelfUnregisteringTickable self = null!;
            self = new SelfUnregisteringTickable(_runner, () => self);
            _runner.Register(self);
            _runner.Register(sibling);

            InvokeUpdate();

            Assert.AreEqual(1, self.TickCount);
            Assert.AreEqual(1, sibling.TickCount);

            InvokeUpdate();
            Assert.AreEqual(1, self.TickCount, "self unregistered itself after first tick");
            Assert.AreEqual(2, sibling.TickCount);
        }

        private void InvokeUpdate()
        {
            // Reach into the private Update — Unity's player loop doesn't run
            // during edit-mode tests, so we drive the method ourselves.
            typeof(EntityTickRunner)
                .GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(_runner, null);
        }

        private sealed class RecordingTickable : ITickable
        {
            public int TickCount;
            public void Tick(float dt) => TickCount++;
        }

        private sealed class SelfUnregisteringTickable : ITickable
        {
            private readonly EntityTickRunner _runner;
            private readonly System.Func<ITickable> _self;
            public int TickCount;

            public SelfUnregisteringTickable(EntityTickRunner runner, System.Func<ITickable> self)
            {
                _runner = runner;
                _self = self;
            }

            public void Tick(float dt)
            {
                TickCount++;
                _runner.Unregister(_self());
            }
        }
    }
}
