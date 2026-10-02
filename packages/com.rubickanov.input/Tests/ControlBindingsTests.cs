using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Rubickanov.Input.Tests
{
    public sealed class ControlBindingsTests : InputTestFixture
    {
        private static readonly RebindOptions Options = new("Gameplay/Move", "Gameplay/Jump", "Gameplay/Sprint",
            "Gameplay/Crouch", "Menu/Restart");

        private Keyboard _keyboard;
        private Mouse _mouse;
        private Gamepad _pad;
        private InputActionAsset _input;
        private InputActionMap _gameplay;
        private InputActionMap _menu;
        private InputBlocker _blocker;
        private ControlBindings _bindings;
        private int _changes;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _pad = InputSystem.AddDevice<Gamepad>();
            _input = TestInput.Create();
            _gameplay = _input.FindActionMap("Gameplay", true);
            _menu = _input.FindActionMap("Menu", true);
            _blocker = new InputBlocker(_input);
            _bindings = new ControlBindings(_input, _blocker, Options);
            _bindings.Changed.Subscribe(_ => _changes++);
            _changes = 0;
        }

        public override void TearDown()
        {
            _bindings.Dispose();
            _blocker.Dispose();
            TestInput.Destroy(_input);
            base.TearDown();
        }

        [Test]
        public void Slots_RebindableActions_OnePerKeyAndScheme()
        {
            string[] ids = _bindings.Slots.Select(slot => slot.Id).ToArray();

            Assert.That(ids, Is.EqualTo(new[]
            {
                "move.up", "move.down", "move.left", "move.right", "jump", "sprint", "crouch", "restart",
                "jump.pad", "sprint.pad", "crouch.pad", "restart.pad",
            }), "keyboard's first; no button stands in for a stick bound whole");
            Assert.That(_bindings.Find("JUMP.PAD").Scheme, Is.EqualTo(ControlScheme.Pad));
            Assert.That(_bindings.Find("move.up").Part, Is.EqualTo("Up"));
            Assert.That(_bindings.Find("interact"), Is.Null, "not rebindable");
        }

        [Test]
        public void Slots_TwoBindingsOfOneScheme_TheSecondGetsANumber()
        {
            using var bindings = new ControlBindings(_input, _blocker, new RebindOptions("Menu/Submit"));

            Assert.That(bindings.Slots.Select(slot => slot.Id), Is.EqualTo(new[] { "submit", "submit.2", "submit.pad" }));
        }

        [Test]
        public void Slots_NoControlSchemes_GoByTheDeviceOfEachPath()
        {
            InputActionAsset bare = TestInput.Create(schemes: false);
            try
            {
                using var blocker = new InputBlocker(bare);
                using var bindings = new ControlBindings(bare, blocker, Options);

                Assert.That(bindings.Find("jump").Scheme, Is.EqualTo(ControlScheme.Keys));
                Assert.That(bindings.Find("jump.pad").Scheme, Is.EqualTo(ControlScheme.Pad));
                Assert.That(bindings.Slots.Count, Is.EqualTo(_bindings.Slots.Count));
            }
            finally
            {
                TestInput.Destroy(bare);
            }
        }

        [Test]
        public void ListenAsync_KeyPressed_BindsItAndSaysSo()
        {
            RebindResult result = Listen("jump", () => Press(_keyboard.fKey));

            Assert.That(result.Kind, Is.EqualTo(RebindKind.Bound));
            Assert.That(_bindings.Current(Slot("jump")), Is.EqualTo("<Keyboard>/f"));
            Assert.That(_changes, Is.EqualTo(1));
            Assert.That(_gameplay.enabled && _menu.enabled, Is.True, "the maps are back");
            Assert.That(_bindings.IsListening, Is.False);
        }

        [Test]
        public void ListenAsync_WhileListening_EveryMapIsBlocked()
        {
            UniTask<RebindResult> task = _bindings.ListenAsync(Slot("jump"), CancellationToken.None);

            Assert.That(_bindings.IsListening, Is.True);
            Assert.That(_gameplay.enabled || _menu.enabled, Is.False);

            Press(_keyboard.escapeKey);
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Succeeded));
        }

        [Test]
        public void ListenAsync_EscOrStart_BacksOut()
        {
            Assert.That(Listen("jump", () => Press(_keyboard.escapeKey)).Kind, Is.EqualTo(RebindKind.Cancelled));
            Release(_keyboard.escapeKey);
            Assert.That(Listen("jump", () => Press(_pad.startButton)).Kind, Is.EqualTo(RebindKind.Cancelled),
                "a keyboard slot too");

            Assert.That(_bindings.Current(Slot("jump")), Is.EqualTo("<Keyboard>/space"));
            Assert.That(_gameplay.enabled && _menu.enabled, Is.True);
            Assert.That(_changes, Is.EqualTo(0));
        }

        [Test]
        public void ListenAsync_MouseMoving_NeverBindsItsButtonsDo()
        {
            UniTask<RebindResult> task = _bindings.ListenAsync(Slot("jump"), CancellationToken.None);
            Set(_mouse.delta, new Vector2(50f, 20f));
            Set(_mouse.position, new Vector2(400f, 300f));
            Set(_mouse.scroll, new Vector2(0f, 120f));
            Settle();
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Pending));

            Press(_mouse.rightButton);
            Settle();

            Assert.That(task.GetAwaiter().GetResult().Kind, Is.EqualTo(RebindKind.Bound));
            Assert.That(_bindings.Current(Slot("jump")), Is.EqualTo("<Mouse>/rightButton"));
        }

        [Test]
        public void ListenAsync_KeyboardSlot_TakesNoPadButton()
        {
            UniTask<RebindResult> task = _bindings.ListenAsync(Slot("jump"), CancellationToken.None);
            Press(_pad.buttonSouth);
            Settle();
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Pending));

            Press(_keyboard.gKey);
            Settle();

            Assert.That(_bindings.Current(Slot("jump")), Is.EqualTo("<Keyboard>/g"));
        }

        [Test]
        public void ListenAsync_PadSlot_TakesOnlyThePad()
        {
            RebindSlot slot = Slot("jump.pad");

            UniTask<RebindResult> task = _bindings.ListenAsync(slot, CancellationToken.None);
            Press(_keyboard.fKey);
            Settle();
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Pending));

            Press(_pad.buttonNorth);
            Settle();

            Assert.That(task.GetAwaiter().GetResult().Kind, Is.EqualTo(RebindKind.Bound));
            Assert.That(_bindings.Current(slot), Is.EqualTo("<Gamepad>/buttonNorth"));
            Assert.That(_bindings.Current(Slot("jump")), Is.EqualTo("<Keyboard>/space"));
        }

        [Test]
        public void Swap_TakenKey_ChangesNothingUntilThePlayerAgrees()
        {
            RebindResult taken = Listen("jump", () => Press(_keyboard.leftShiftKey));

            Assert.That(taken.Kind, Is.EqualTo(RebindKind.Taken));
            Assert.That(taken.Other, Is.SameAs(Slot("sprint")));
            Assert.That(_bindings.Current(Slot("jump")), Is.EqualTo("<Keyboard>/space"));
            Assert.That(_bindings.Current(Slot("sprint")), Is.EqualTo("<Keyboard>/leftShift"));
            Assert.That(_changes, Is.EqualTo(0));

            RebindResult swapped = _bindings.Swap(taken);

            Assert.That(swapped.Kind, Is.EqualTo(RebindKind.Bound));
            Assert.That(swapped.Other, Is.SameAs(Slot("sprint")));
            Assert.That(_bindings.Current(Slot("jump")), Is.EqualTo("<Keyboard>/leftShift"));
            Assert.That(_bindings.Current(Slot("sprint")), Is.EqualTo("<Keyboard>/space"));
            Assert.That(_changes, Is.EqualTo(1), "one change for both");
        }

        [Test]
        public void Swap_KeyFreedSince_BindsAsSetWould()
        {
            RebindResult taken = _bindings.Set(Slot("jump"), "<Keyboard>/leftShift");
            _bindings.Set(Slot("sprint"), "<Keyboard>/k");

            RebindResult result = _bindings.Swap(taken);

            Assert.That(result.Kind, Is.EqualTo(RebindKind.Bound));
            Assert.That(result.Other, Is.Null);
            Assert.That(_bindings.Current(Slot("jump")), Is.EqualTo("<Keyboard>/leftShift"));
        }

        [Test]
        public void Set_OneWayOfAComposite_ChangesAlone()
        {
            Assert.That(_bindings.Set(Slot("move.up"), "<Keyboard>/i").Kind, Is.EqualTo(RebindKind.Bound));

            Assert.That(_bindings.Current(Slot("move.up")), Is.EqualTo("<Keyboard>/i"));
            Assert.That(_bindings.Current(Slot("move.down")), Is.EqualTo("<Keyboard>/s"));
        }

        [Test]
        public void Set_DirectionOfAStickOrDpad_RefusedByAPadSlot()
        {
            foreach (string path in new[] { "<Gamepad>/leftStick/up", "<Gamepad>/rightStick/left", "<Gamepad>/dpad/up" })
            {
                Assert.That(_bindings.Set(Slot("jump.pad"), path).Kind, Is.EqualTo(RebindKind.Refused), path);
            }

            Assert.That(_bindings.Set(Slot("jump.pad"), "<Gamepad>/leftStickPress").Kind, Is.Not.EqualTo(RebindKind.Refused));
        }

        [Test]
        public void Set_SameKeyOrAnotherDevices_UnchangedOrRefused()
        {
            Assert.That(_bindings.Set(Slot("jump"), "<Keyboard>/space").Kind, Is.EqualTo(RebindKind.Unchanged));
            Assert.That(_bindings.Set(Slot("jump"), "<Gamepad>/buttonSouth").Kind, Is.EqualTo(RebindKind.Refused));
            Assert.That(_bindings.Set(Slot("jump"), "<Keyboard>/escape").Kind, Is.EqualTo(RebindKind.Refused));
            Assert.That(_bindings.Set(Slot("jump"), "<Mouse>/delta").Kind, Is.EqualTo(RebindKind.Refused));
            Assert.That(_bindings.Set(Slot("jump.pad"), "<Gamepad>/start").Kind, Is.EqualTo(RebindKind.Refused));
            Assert.That(_changes, Is.EqualTo(0));
        }

        [Test]
        public void Reset_DefaultTakenByAnotherSlot_ComesBackTaken()
        {
            _bindings.Set(Slot("jump"), "<Keyboard>/f");
            Assert.That(_bindings.Reset(Slot("jump")).Kind, Is.EqualTo(RebindKind.Bound));
            Assert.That(_bindings.IsDefault(Slot("jump")), Is.True);

            _bindings.Set(Slot("jump"), "<Keyboard>/f");
            _bindings.Set(Slot("sprint"), "<Keyboard>/space");
            RebindResult reset = _bindings.Reset(Slot("jump"));
            Assert.That(reset.Kind, Is.EqualTo(RebindKind.Taken));
            Assert.That(reset.Other, Is.SameAs(Slot("sprint")));

            _bindings.ResetAll();
            foreach (RebindSlot slot in _bindings.Slots)
            {
                Assert.That(_bindings.IsDefault(slot), Is.True, slot.Id);
            }
        }

        [Test]
        public void ResetAll_Scheme_LeavesTheOtherSchemesKeys()
        {
            _bindings.Set(Slot("jump"), "<Keyboard>/f");
            _bindings.Set(Slot("move.up"), "<Keyboard>/i");
            _bindings.Set(Slot("jump.pad"), "<Gamepad>/buttonNorth");

            _bindings.ResetAll(ControlScheme.Keys);

            Assert.That(_bindings.IsDefault(Slot("jump")), Is.True);
            Assert.That(_bindings.IsDefault(Slot("move.up")), Is.True);
            Assert.That(_bindings.Current(Slot("jump.pad")), Is.EqualTo("<Gamepad>/buttonNorth"));

            _bindings.Set(Slot("jump"), "<Keyboard>/f");
            _bindings.ResetAll(ControlScheme.Pad);

            Assert.That(_bindings.IsDefault(Slot("jump.pad")), Is.True);
            Assert.That(_bindings.Current(Slot("jump")), Is.EqualTo("<Keyboard>/f"));
        }

        [Test]
        public void ToJson_ChangedKeys_OnlyThoseBySlotId()
        {
            Assert.That(_bindings.ToJson(), Is.EqualTo("{\n  \"format\": 1,\n  \"keys\": {}\n}\n"));

            _bindings.Set(Slot("jump"), "<Keyboard>/f");
            _bindings.Set(Slot("jump.pad"), "<Gamepad>/buttonNorth");

            Assert.That(_bindings.ToJson(), Is.EqualTo(
                "{\n  \"format\": 1,\n  \"keys\": {\n    \"jump\": \"<Keyboard>/f\",\n    \"jump.pad\": \"<Gamepad>/buttonNorth\"\n  }\n}\n"));
        }

        [Test]
        public void LoadJson_WhatToJsonWrote_GivesTheKeysBack()
        {
            _bindings.Set(Slot("jump"), "<Keyboard>/f");
            _bindings.Set(Slot("move.up"), "<Keyboard>/i");
            string json = _bindings.ToJson();
            InputActionAsset input = TestInput.Create();
            try
            {
                using var blocker = new InputBlocker(input);
                using var again = new ControlBindings(input, blocker, Options);

                again.LoadJson(json);

                Assert.That(again.Current(again.Find("jump")), Is.EqualTo("<Keyboard>/f"));
                Assert.That(input.FindAction("Gameplay/Move").bindings[1].effectivePath, Is.EqualTo("<Keyboard>/i"));
                Assert.That(again.ToJson(), Is.EqualTo(json));
            }
            finally
            {
                TestInput.Destroy(input);
            }
        }

        [Test]
        public void LoadJson_SlotTheJsonLeavesOut_GoesBackToItsDefault()
        {
            _bindings.Set(Slot("sprint"), "<Keyboard>/k");

            _bindings.LoadJson("{ \"format\": 1, \"keys\": { \"jump\": \"<Keyboard>/f\" } }");

            Assert.That(_bindings.IsDefault(Slot("sprint")), Is.True);
            Assert.That(_bindings.Current(Slot("jump")), Is.EqualTo("<Keyboard>/f"));
        }

        [Test]
        public void LoadJson_NotTheKeysJson_ThrowsAndChangesNothing()
        {
            _bindings.Set(Slot("jump"), "<Keyboard>/f");

            Assert.Throws<FormatException>(() => _bindings.LoadJson("{ \"format\": 1, \"keys\": "));
            Assert.Throws<FormatException>(() => _bindings.LoadJson("{ \"format\": 2, \"keys\": {} }"));
            Assert.Throws<FormatException>(() => _bindings.LoadJson("[1, 2]"));

            Assert.That(_bindings.Current(Slot("jump")), Is.EqualTo("<Keyboard>/f"));
        }

        [Test]
        public void LoadJson_UnknownSlotOrKeyTheSlotRefuses_Dropped()
        {
            LogAssert.Expect(LogType.Warning, new Regex("no key is called nope"));
            LogAssert.Expect(LogType.Warning, new Regex("is not a key crouch takes"));

            _bindings.LoadJson("{ \"format\": 1, \"other\": [true, null, 1.5e3], " +
                               "\"keys\": { \"nope\": \"<Keyboard>/x\", \"crouch\": \"<Gamepad>/buttonSouth\" } }");

            Assert.That(_bindings.Current(Slot("crouch")), Is.EqualTo("<Keyboard>/leftCtrl"));
        }

        [Test]
        public void LoadJson_TwoSlotsOnOneKey_ThatSchemeGoesBackToItsDefaults()
        {
            LogAssert.Expect(LogType.Warning, new Regex("share <Keyboard>/space"));

            _bindings.LoadJson("{ \"format\": 1, \"keys\": { \"crouch\": \"<Keyboard>/space\", \"move.up\": " +
                               "\"<Keyboard>/i\", \"jump.pad\": \"<Gamepad>/buttonNorth\" } }");

            Assert.That(_bindings.Current(Slot("crouch")), Is.EqualTo("<Keyboard>/leftCtrl"), "space is jump's");
            Assert.That(_bindings.IsDefault(Slot("move.up")), Is.True, "the scheme goes back whole");
            Assert.That(_bindings.Current(Slot("jump.pad")), Is.EqualTo("<Gamepad>/buttonNorth"));
            Assert.That(_changes, Is.EqualTo(0), "loading is not a change to keep");
        }

        [Test]
        public void LoadJson_EscapedText_ReadsTheStrings()
        {
            _bindings.LoadJson("{\"keys\":{\"j\\u0075mp\":\"<Keyboard>\\/f\"},\"format\":1}");

            Assert.That(_bindings.Current(Slot("jump")), Is.EqualTo("<Keyboard>/f"));
        }

        [Test]
        public void ListenAsync_CalledOff_LetsTheKeysThrough()
        {
            using var cancel = new CancellationTokenSource();
            UniTask<RebindResult> task = _bindings.ListenAsync(Slot("jump"), cancel.Token);

            cancel.Cancel();

            Assert.That(task.GetAwaiter().GetResult().Kind, Is.EqualTo(RebindKind.Cancelled));
            Assert.That(_bindings.IsListening, Is.False);
            Assert.That(_gameplay.enabled && _menu.enabled, Is.True);

            Press(_keyboard.spaceKey);
            Assert.That(_input.FindAction("Gameplay/Jump").WasPressedThisFrame(), Is.True,
                "the operation no longer swallows it");
        }

        [Test]
        public void ListenAsync_AlreadyListening_Throws()
        {
            UniTask<RebindResult> first = _bindings.ListenAsync(Slot("jump"), CancellationToken.None);

            Assert.Throws<InvalidOperationException>(() => _bindings.ListenAsync(Slot("sprint"), CancellationToken.None));
            Press(_keyboard.escapeKey);
            Assert.That(first.Status, Is.EqualTo(UniTaskStatus.Succeeded));
        }

        [Test]
        public void ListenAsync_Disposed_CancelsTheListening()
        {
            UniTask<RebindResult> task = _bindings.ListenAsync(Slot("jump"), CancellationToken.None);

            _bindings.Dispose();

            Assert.That(task.GetAwaiter().GetResult().Kind, Is.EqualTo(RebindKind.Cancelled));
            Assert.That(_gameplay.enabled && _menu.enabled, Is.True);
        }

        private RebindSlot Slot(string id) => _bindings.Find(id);

        // Starts listening, presses, lets the operation's wait for a stronger control pass.
        private RebindResult Listen(string id, Action press)
        {
            UniTask<RebindResult> task = _bindings.ListenAsync(Slot(id), CancellationToken.None);
            press();
            Settle();
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            return task.GetAwaiter().GetResult();
        }

        private void Settle()
        {
            currentTime += 1.0;
            InputSystem.Update();
        }
    }
}
