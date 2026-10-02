using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Rubickanov.Input
{
    /// <summary>
    /// What the package reads off an input, whatever form it comes in: an <see cref="InputActionAsset"/>, a generated
    /// C# class over one, a lone map.
    /// </summary>
    internal static class InputMaps
    {
        /// <summary>The maps the input's actions belong to, in their order.</summary>
        public static List<InputActionMap> Of(IInputActionCollection2 input)
        {
            var maps = new List<InputActionMap>();
            if (input is InputActionAsset asset)
            {
                foreach (InputActionMap map in asset.actionMaps)
                {
                    maps.Add(map);
                }

                return maps;
            }

            if (input is InputActionMap single)
            {
                maps.Add(single);
                return maps;
            }

            foreach (InputAction action in input)
            {
                InputActionMap? map = action.actionMap;
                if (map != null && !maps.Contains(map))
                {
                    maps.Add(map);
                }
            }

            return maps;
        }

        /// <summary>Whether <paramref name="changed"/> (an action, a map or an asset) is part of <paramref name="maps"/>.</summary>
        public static bool Touches(List<InputActionMap> maps, object changed)
        {
            switch (changed)
            {
                case InputAction action:
                    return action.actionMap != null && maps.Contains(action.actionMap);
                case InputActionMap map:
                    return maps.Contains(map);
                case InputActionAsset asset:
                    foreach (InputActionMap map in maps)
                    {
                        if (map.asset == asset)
                        {
                            return true;
                        }
                    }

                    return false;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The binding group of <paramref name="scheme"/>: of the input's control schemes, the one that needs a pad, or a
        /// keyboard or a mouse; null when the input has none such.
        /// </summary>
        public static string? GroupOf(IInputActionCollection2 input, ControlScheme scheme)
        {
            foreach (InputControlScheme controlScheme in input.controlSchemes)
            {
                foreach (InputControlScheme.DeviceRequirement requirement in controlScheme.deviceRequirements)
                {
                    if (SchemeOfLayout(InputControlPath.TryGetDeviceLayout(requirement.controlPath)) == scheme)
                    {
                        return controlScheme.bindingGroup;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Whether <paramref name="binding"/> (not a composite) is of <paramref name="scheme"/>: by its group when it has
        /// one and the input names the scheme's group, else by the device of its path.
        /// </summary>
        public static bool IsOf(InputBinding binding, ControlScheme scheme, string? group)
        {
            if (group != null && !string.IsNullOrEmpty(binding.groups))
            {
                return InputBinding.MaskByGroup(group).Matches(binding);
            }

            string path = binding.effectivePath;
            return !string.IsNullOrEmpty(path) && SchemeOfLayout(InputControlPath.TryGetDeviceLayout(path)) == scheme;
        }

        /// <summary>The scheme a device layout plays: a pad, or the keyboard and mouse; null for anything else.</summary>
        public static ControlScheme? SchemeOfLayout(string? layout)
        {
            if (string.IsNullOrEmpty(layout))
            {
                return null;
            }

            if (Based(layout!, "Gamepad"))
            {
                return ControlScheme.Pad;
            }

            return Based(layout!, "Keyboard") || Based(layout!, "Mouse") || Based(layout!, "Pointer")
                ? ControlScheme.Keys
                : null;
        }

        private static bool Based(string layout, string on) => InputSystem.IsFirstLayoutBasedOnSecond(layout, on);
    }
}
