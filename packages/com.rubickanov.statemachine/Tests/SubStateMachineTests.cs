using System.Collections.Generic;
using NUnit.Framework;

namespace Rubickanov.StateMachine.Tests
{
    [TestFixture]
    public class SubStateMachineTests
    {
        private enum Parent { Menu, Combat, Paused }
        private enum Combat { Aiming, Firing, Reloading }

        private List<string> _log;

        [SetUp]
        public void SetUp()
        {
            _log = new List<string>();
        }

        private RecordingState NewState(string name) => new RecordingState(name, _log);

        [Test]
        public void OnEnter_AsIState_StartsAtInitialState()
        {
            var sub = new SubStateMachine<Combat>(Combat.Aiming);
            sub.AddState(Combat.Aiming, NewState("Aim"));
            sub.AddState(Combat.Firing, NewState("Fire"));

            ((IState)sub).OnEnter();

            Assert.IsTrue(sub.IsStarted);
            Assert.AreEqual(Combat.Aiming, sub.CurrentKey);
            CollectionAssert.AreEqual(new[] { "Aim:Enter" }, _log);
        }

        [Test]
        public void OnUpdate_AsIState_TicksCurrentChildState()
        {
            var sub = new SubStateMachine<Combat>(Combat.Aiming);
            sub.AddState(Combat.Aiming, NewState("Aim"));
            ((IState)sub).OnEnter();
            _log.Clear();

            ((IState)sub).OnUpdate(0.1f);

            CollectionAssert.AreEqual(new[] { "Aim:Update" }, _log);
        }

        [Test]
        public void OnExit_AsIState_StopsSubMachine()
        {
            var sub = new SubStateMachine<Combat>(Combat.Aiming);
            sub.AddState(Combat.Aiming, NewState("Aim"));
            ((IState)sub).OnEnter();

            ((IState)sub).OnExit();

            Assert.IsFalse(sub.IsStarted);
            CollectionAssert.AreEqual(new[] { "Aim:Enter", "Aim:Exit" }, _log);
        }

        [Test]
        public void ParentTransition_IntoSub_StartsSubAtInitialState()
        {
            var parent = new StateMachine<Parent>();
            var combatSub = new SubStateMachine<Combat>(Combat.Aiming);
            combatSub.AddState(Combat.Aiming, NewState("Aim"));
            combatSub.AddState(Combat.Firing, NewState("Fire"));

            parent.AddState(Parent.Menu, NewState("Menu"));
            parent.AddState(Parent.Combat, combatSub);
            parent.Start(Parent.Menu);
            _log.Clear();

            parent.SetState(Parent.Combat);

            Assert.IsTrue(combatSub.IsStarted);
            Assert.AreEqual(Combat.Aiming, combatSub.CurrentKey);
            CollectionAssert.AreEqual(new[] { "Menu:Exit", "Aim:Enter" }, _log);
        }

        [Test]
        public void ParentTransition_OutOfSub_StopsChildBeforeNextParentEnter()
        {
            var parent = new StateMachine<Parent>();
            var combatSub = new SubStateMachine<Combat>(Combat.Aiming);
            combatSub.AddState(Combat.Aiming, NewState("Aim"));

            parent.AddState(Parent.Menu, NewState("Menu"));
            parent.AddState(Parent.Combat, combatSub);
            parent.Start(Parent.Combat);
            _log.Clear();

            parent.SetState(Parent.Menu);

            Assert.IsFalse(combatSub.IsStarted);
            CollectionAssert.AreEqual(new[] { "Aim:Exit", "Menu:Enter" }, _log);
        }

        [Test]
        public void SubMachine_Reentered_RestartsAtInitialState()
        {
            var parent = new StateMachine<Parent>();
            var combatSub = new SubStateMachine<Combat>(Combat.Aiming);
            combatSub.AddState(Combat.Aiming, NewState("Aim"));
            combatSub.AddState(Combat.Firing, NewState("Fire"));

            parent.AddState(Parent.Menu, NewState("Menu"));
            parent.AddState(Parent.Combat, combatSub);
            parent.Start(Parent.Menu);

            parent.SetState(Parent.Combat);
            combatSub.SetState(Combat.Firing);
            parent.SetState(Parent.Menu);
            _log.Clear();

            parent.SetState(Parent.Combat);

            Assert.AreEqual(Combat.Aiming, combatSub.CurrentKey);
            CollectionAssert.AreEqual(new[] { "Menu:Exit", "Aim:Enter" }, _log);
        }

        [Test]
        public void ChildTransition_InsideSub_DoesNotFireParentStateChanged()
        {
            var parent = new StateMachine<Parent>();
            var combatSub = new SubStateMachine<Combat>(Combat.Aiming);
            combatSub.AddState(Combat.Aiming, NewState("Aim"));
            combatSub.AddState(Combat.Firing, NewState("Fire"));

            parent.AddState(Parent.Combat, combatSub);
            parent.Start(Parent.Combat);

            var parentFireCount = 0;
            parent.StateChanged += (_, __) => parentFireCount++;

            combatSub.SetState(Combat.Firing);

            Assert.AreEqual(0, parentFireCount);
            Assert.AreEqual(Combat.Firing, combatSub.CurrentKey);
        }

        private class RecordingState : StateBase
        {
            private readonly string _name;
            private readonly List<string> _log;

            public RecordingState(string name, List<string> log)
            {
                _name = name;
                _log = log;
            }

            public override void OnEnter() => _log.Add(_name + ":Enter");
            public override void OnUpdate(float deltaTime) => _log.Add(_name + ":Update");
            public override void OnExit() => _log.Add(_name + ":Exit");
        }
    }
}
