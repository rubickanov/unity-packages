using System.Text.RegularExpressions;
using NUnit.Framework;
using Rubickanov.Save;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Rubickanov.Input.Tests
{
    public sealed class SavedControlBindingsTests
    {
        private InputActionAsset _input;
        private InputBlocker _blocker;
        private ControlBindings _bindings;
        private MemorySaveStore _store;

        [SetUp]
        public void SetUp()
        {
            _store = new MemorySaveStore();
            (_input, _blocker, _bindings) = Create();
        }

        [TearDown]
        public void TearDown() => Destroy(_input, _blocker, _bindings);

        [Test]
        public void Changed_KeyBound_WritesControlsJson()
        {
            using var saved = new SavedControlBindings(_bindings, _store);

            _bindings.Set(_bindings.Find("jump"), "<Keyboard>/f");

            StringAssert.Contains("\"jump\": \"<Keyboard>/f\"", Read(SavedControlBindings.Key));
            Assert.That(SavedControlBindings.Key.Name, Is.EqualTo("controls.json"));
        }

        [Test]
        public void Constructor_KeptKeys_GivesThemBack()
        {
            using (new SavedControlBindings(_bindings, _store))
            {
                _bindings.Set(_bindings.Find("jump.pad"), "<Gamepad>/buttonNorth");
            }

            (InputActionAsset input, InputBlocker blocker, ControlBindings again) = Create();
            try
            {
                using var saved = new SavedControlBindings(again, _store);

                Assert.That(again.Current(again.Find("jump.pad")), Is.EqualTo("<Gamepad>/buttonNorth"));
            }
            finally
            {
                Destroy(input, blocker, again);
            }
        }

        [Test]
        public void Constructor_BrokenText_KeptAsideAndTheDefaultsStay()
        {
            _store.WriteTextAsync(SavedControlBindings.Key, "{ \"format\": 1, \"keys\": ").GetAwaiter().GetResult();
            LogAssert.Expect(LogType.Error, new Regex("could not be read"));

            using var saved = new SavedControlBindings(_bindings, _store);

            Assert.That(_bindings.IsDefault(_bindings.Find("jump")), Is.True);
            Assert.That(Read(new SaveKey(SavedControlBindings.Key.Area, "controls.broken.json")),
                Is.EqualTo("{ \"format\": 1, \"keys\": "));
        }

        [Test]
        public void For_SecondSeat_KeepsItsKeysBesideTheFirsts()
        {
            using var saved = new SavedControlBindings(_bindings, _store, SavedControlBindings.For(1));

            _bindings.Set(_bindings.Find("jump"), "<Keyboard>/f");

            Assert.That(SavedControlBindings.For(0), Is.EqualTo(SavedControlBindings.Key));
            Assert.That(SavedControlBindings.For(1).Name, Is.EqualTo("controls.p2.json"));
            StringAssert.Contains("\"jump\": \"<Keyboard>/f\"", Read(SavedControlBindings.For(1)));
            Assert.That(Read(SavedControlBindings.Key), Is.Null);
        }

        private string Read(SaveKey key) => _store.ReadTextAsync(key).GetAwaiter().GetResult();

        private static (InputActionAsset, InputBlocker, ControlBindings) Create()
        {
            var input = ScriptableObject.CreateInstance<InputActionAsset>();
            InputAction jump = input.AddActionMap("Gameplay").AddAction("Jump", InputActionType.Button);
            jump.AddBinding("<Keyboard>/space");
            jump.AddBinding("<Gamepad>/buttonSouth");
            var blocker = new InputBlocker(input);
            return (input, blocker, new ControlBindings(input, blocker, new RebindOptions("Gameplay/Jump")));
        }

        private static void Destroy(InputActionAsset input, InputBlocker blocker, ControlBindings bindings)
        {
            bindings.Dispose();
            blocker.Dispose();
            Object.DestroyImmediate(input);
        }
    }
}
