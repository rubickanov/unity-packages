using UnityEngine;
using UnityEngine.InputSystem;

namespace Rubickanov.Input.Tests
{
    /// <summary>
    /// A small game's input built in code: a Gameplay and a Menu map with keyboard-and-mouse and pad bindings in two
    /// control schemes, or the same bindings without schemes or groups.
    /// </summary>
    internal static class TestInput
    {
        private const string Keys = "Keyboard&Mouse";
        private const string Pad = "Gamepad";

        public static InputActionAsset Create(bool schemes = true)
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "TestInput";
            string keys = schemes ? Keys : null;
            string pad = schemes ? Pad : null;

            InputActionMap gameplay = asset.AddActionMap("Gameplay");
            InputAction move = gameplay.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w", keys)
                .With("Down", "<Keyboard>/s", keys)
                .With("Left", "<Keyboard>/a", keys)
                .With("Right", "<Keyboard>/d", keys);
            move.AddBinding("<Gamepad>/leftStick", groups: pad);
            InputAction look = gameplay.AddAction("Look", InputActionType.Value, expectedControlLayout: "Vector2");
            look.AddBinding("<Mouse>/delta", groups: keys);
            look.AddBinding("<Gamepad>/rightStick", groups: pad);
            Button(gameplay, "Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth", keys, pad);
            Button(gameplay, "Sprint", "<Keyboard>/leftShift", "<Gamepad>/rightTrigger", keys, pad);
            Button(gameplay, "Crouch", "<Keyboard>/leftCtrl", "<Gamepad>/buttonEast", keys, pad);
            Button(gameplay, "Interact", "<Keyboard>/e", "<Gamepad>/buttonWest", keys, pad);
            InputAction zoom = gameplay.AddAction("Zoom", InputActionType.Value, expectedControlLayout: "Axis");
            zoom.AddBinding("<Mouse>/scroll/y", groups: keys);
            zoom.AddCompositeBinding("1DAxis")
                .With("Negative", "<Gamepad>/dpad/down", pad)
                .With("Positive", "<Gamepad>/dpad/up", pad);

            InputActionMap menu = asset.AddActionMap("Menu");
            InputAction navigate = menu.AddAction("Navigate", InputActionType.Value, expectedControlLayout: "Vector2");
            navigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow", keys)
                .With("Down", "<Keyboard>/downArrow", keys)
                .With("Left", "<Keyboard>/leftArrow", keys)
                .With("Right", "<Keyboard>/rightArrow", keys);
            navigate.AddBinding("<Gamepad>/leftStick", groups: pad);
            navigate.AddBinding("<Gamepad>/dpad", groups: pad);
            InputAction submit = Button(menu, "Submit", "<Keyboard>/enter", "<Gamepad>/buttonSouth", keys, pad);
            submit.AddBinding("<Keyboard>/numpadEnter", groups: keys);
            Button(menu, "Cancel", "<Keyboard>/escape", "<Gamepad>/buttonEast", keys, pad);
            Button(menu, "Pause", "<Keyboard>/escape", "<Gamepad>/start", keys, pad);
            Button(menu, "Restart", "<Keyboard>/r", "<Gamepad>/select", keys, pad);
            InputAction tab = menu.AddAction("Tab", InputActionType.Value, expectedControlLayout: "Axis");
            tab.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/q", keys)
                .With("Positive", "<Keyboard>/e", keys);
            tab.AddCompositeBinding("1DAxis")
                .With("Negative", "<Gamepad>/leftShoulder", pad)
                .With("Positive", "<Gamepad>/rightShoulder", pad);

            if (schemes)
            {
                asset.AddControlScheme(Keys).WithRequiredDevice("<Keyboard>").WithRequiredDevice("<Mouse>");
                asset.AddControlScheme(Pad).WithRequiredDevice("<Gamepad>");
            }

            return asset;
        }

        public static void Destroy(InputActionAsset asset)
        {
            asset.Disable();
            Object.DestroyImmediate(asset);
        }

        private static InputAction Button(InputActionMap map, string name, string key, string button, string keys, string pad)
        {
            InputAction action = map.AddAction(name, InputActionType.Button);
            action.AddBinding(key, groups: keys);
            action.AddBinding(button, groups: pad);
            return action;
        }
    }
}
