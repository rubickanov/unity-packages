using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rubickanov.DevConsole.Tests
{
    [TestFixture]
    public class CommandBindingsTests : InputTestFixture
    {
        private string _config = null!;
        private CommandRegistry _registry = null!;
        private Keyboard _keyboard = null!;
        private GameObject _host = null!;
        private CommandBindings _bindings = null!;
        private readonly List<string> _ran = new();

        public override void Setup()
        {
            base.Setup();
            _config = TestConfig.Use();
            _registry = new CommandRegistry();
            Commands.ConsoleCommands.Register(_registry);
            _registry.Register("probe", args => _ran.Add(args.Length > 0 ? args[0] : ""));
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _host = new GameObject("[DevConsole] bindings test");
            _bindings = _host.AddComponent<CommandBindings>();
        }

        public override void TearDown()
        {
            CommandBindings.Suppress = null;
            Object.DestroyImmediate(_host);
            TestConfig.Release(_config);
            _ran.Clear();
            base.TearDown();
        }

        [Test]
        public void Tick_BoundKeyPressed_RunsItsCommand()
        {
            _registry.Bindings.Set(new KeyChord(Key.F5), "probe quicksave");
            Press(_keyboard.f5Key);

            _bindings.Tick(_registry, _keyboard);

            CollectionAssert.AreEqual(new[] { "quicksave" }, _ran);
        }

        [Test]
        public void Tick_NothingPressed_RunsNothing()
        {
            _registry.Bindings.Set(new KeyChord(Key.F5), "probe quicksave");

            _bindings.Tick(_registry, _keyboard);

            CollectionAssert.IsEmpty(_ran);
        }

        [Test]
        public void Tick_ConsoleOpen_RunsNothing()
        {
            _registry.Bindings.Set(new KeyChord(Key.F5), "probe quicksave");
            var console = new GameObject("[DevConsole] window test").AddComponent<DevConsoleWindow>();
            console.Attach();
            console.SetOpen(true);
            Press(_keyboard.f5Key);

            try
            {
                _bindings.Tick(_registry, _keyboard);
            }
            finally
            {
                console.Detach();
                Object.DestroyImmediate(console.gameObject);
            }

            CollectionAssert.IsEmpty(_ran);
        }

        [Test]
        public void Tick_Suppressed_RunsNothing()
        {
            _registry.Bindings.Set(new KeyChord(Key.F5), "probe quicksave");
            CommandBindings.Suppress = () => true;
            Press(_keyboard.f5Key);

            _bindings.Tick(_registry, _keyboard);

            CollectionAssert.IsEmpty(_ran);
        }

        [Test]
        public void Tick_BoundCommandChangesTheBindings_RunsWithoutThrowing()
        {
            _registry.Bindings.Set(new KeyChord(Key.F5), "bind clear");
            _registry.Bindings.Set(new KeyChord(Key.F6), "probe other");
            Press(_keyboard.f5Key);

            Assert.DoesNotThrow(() => _bindings.Tick(_registry, _keyboard));
            Assert.AreEqual(0, _registry.Bindings.Bindings.Count);
        }

        [Test]
        public void Tick_BindingOnAKeyTheKeyboardDoesNotHave_LeavesTheOthersWorking()
        {
            _registry.Bindings.Set(new KeyChord((Key)9999), "probe ghost");
            _registry.Bindings.Set(new KeyChord(Key.F5), "probe quicksave");
            Press(_keyboard.f5Key);

            _bindings.Tick(_registry, _keyboard);

            CollectionAssert.AreEqual(new[] { "quicksave" }, _ran);
        }
    }
}
