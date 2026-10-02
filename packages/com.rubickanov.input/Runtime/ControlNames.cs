using System;
using System.Collections.Generic;
using R3;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace Rubickanov.Input
{
    /// <summary>
    /// What an input's actions are bound to, as names (<see cref="KeyNames"/>) for the device the player is on
    /// (<see cref="ActiveDevice"/>), with the player's own keys (binding overrides). An action's first binding of the
    /// scheme is the one named; a composite gives its parts in reading order (W A S D). <see cref="Changed"/> fires when
    /// any name could read otherwise. One per input.
    /// </summary>
    public sealed class ControlNames : IDisposable
    {
        // A 2D composite reads up, left, down, right: W A S D.
        private static readonly string[] VectorOrder = { "up", "left", "down", "right" };

        private readonly IInputActionCollection2 _input;
        private readonly List<InputActionMap> _maps;
        private readonly string? _keysGroup;
        private readonly string? _padGroup;
        private readonly Subject<Unit> _changed = new();
        private readonly IDisposable _deviceChanged;
        private bool _disposed;

        public ControlNames(IInputActionCollection2 input, ActiveDevice device)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            Device = device ?? throw new ArgumentNullException(nameof(device));
            _maps = InputMaps.Of(input);
            _keysGroup = InputMaps.GroupOf(input, ControlScheme.Keys);
            _padGroup = InputMaps.GroupOf(input, ControlScheme.Pad);
            _deviceChanged = device.Changed.Subscribe(_ => _changed.OnNext(Unit.Default));
            InputSystem.onActionChange += OnActionChange;
        }

        public ActiveDevice Device { get; }

        /// <summary>The device's scheme changed, a pad of another family came in, a key was rebound, the layout switched.</summary>
        public Observable<Unit> Changed => _changed;

        /// <summary>The action named "Map/Action" or "Action"; throws for a name the input has not.</summary>
        public InputAction Action(string name) => _input.FindAction(name, true);

        /// <summary>The keys of the action named <paramref name="action"/> for the device in hand.</summary>
        public IReadOnlyList<KeyName> Keys(string action, string? part = null) => Keys(Action(action), Device.Scheme, part);

        /// <summary>The keys of <paramref name="action"/> for the device in hand.</summary>
        public IReadOnlyList<KeyName> Keys(InputAction action, string? part = null) => Keys(action, Device.Scheme, part);

        /// <summary>
        /// The keys of <paramref name="action"/>'s first binding of <paramref name="scheme"/>: one for a plain binding, the
        /// parts of a composite (only <paramref name="part"/>, if given); none when the scheme has no binding for it.
        /// </summary>
        public IReadOnlyList<KeyName> Keys(InputAction action, ControlScheme scheme, string? part = null)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            string? group = Group(scheme);
            ReadOnlyArray<InputBinding> bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                InputBinding binding = bindings[i];
                if (binding.isPartOfComposite)
                {
                    continue;
                }

                if (binding.isComposite)
                {
                    int end = i + 1;
                    bool ours = false;
                    for (; end < bindings.Count && bindings[end].isPartOfComposite; end++)
                    {
                        ours |= InputMaps.IsOf(bindings[end], scheme, group);
                    }

                    // A composite carries no group of its own; its parts do.
                    if (ours)
                    {
                        return Parts(binding, bindings, i + 1, end, part);
                    }

                    continue;
                }

                if (InputMaps.IsOf(binding, scheme, group) && !string.IsNullOrEmpty(binding.effectivePath))
                {
                    if (part == null)
                    {
                        return new[] { Name(binding.effectivePath) };
                    }

                    // A stick or the d-pad bound whole has the part as one of its ways: leftStick/up.
                    return IsWay(part) && action.expectedControlType == "Vector2"
                        ? new[] { Name(binding.effectivePath + "/" + part.ToLowerInvariant()).WithPart(part) }
                        : Array.Empty<KeyName>();
                }
            }

            return Array.Empty<KeyName>();
        }

        /// <summary>The keys of the action named <paramref name="action"/> for the device in hand as one text; "" for none.</summary>
        public string Text(string action, string? part = null) => KeyNames.Join(Keys(action, part));

        /// <summary>The keys of <paramref name="action"/> for the device in hand as one text; "" for none.</summary>
        public string Text(InputAction action, string? part = null) => KeyNames.Join(Keys(action, part));

        /// <summary>The keys of the action named <paramref name="action"/> under <paramref name="scheme"/> as one text.</summary>
        public string Text(string action, ControlScheme scheme, string? part = null) =>
            KeyNames.Join(Keys(Action(action), scheme, part));

        /// <summary>The key at binding path <paramref name="path"/>, named for the player's keyboard and pad.</summary>
        public KeyName Name(string path) => KeyNames.Name(path, Keyboard.current, Device.Family);

        /// <summary>
        /// The name of a key that is not an action's (a text field's Enter), for the device in hand: "" while the player is
        /// on the other scheme, whose hints leave it out.
        /// </summary>
        public string Fixed(string path) => KeyNames.Join(FixedKeys(path));

        /// <summary>The key at binding path <paramref name="path"/> for the device in hand; none on the other scheme.</summary>
        public IReadOnlyList<KeyName> FixedKeys(string path)
        {
            KeyName key = Name(path);
            bool pad = key.Device == KeyDevice.Pad;
            return pad == (Device.Scheme == ControlScheme.Pad) ? new[] { key } : Array.Empty<KeyName>();
        }

        /// <summary>
        /// Whether the player holds <paramref name="key"/> now on the keyboard, the mouse or the pad in hand: a button down, a
        /// stick or trigger past <see cref="ActiveDevice.Actuation"/>, the wheel turning, the mouse moving.
        /// </summary>
        public bool IsDown(KeyName key)
        {
            InputDevice? device = key.Device switch
            {
                KeyDevice.Keyboard => Keyboard.current,
                KeyDevice.Mouse => Mouse.current,
                KeyDevice.Pad => Device.Pad,
                _ => null,
            };
            InputControl? control = device != null && !string.IsNullOrEmpty(key.Control)
                ? device.TryGetChildControl(key.Control)
                : null;
            return control != null && control.IsActuated(ActiveDevice.Actuation);
        }

        /// <summary>
        /// The binding group of <paramref name="scheme"/>: the input's control scheme that needs a pad, or a keyboard or
        /// mouse; null without one, when bindings go by the device of their path.
        /// </summary>
        public string? Group(ControlScheme scheme) => scheme == ControlScheme.Pad ? _padGroup : _keysGroup;

        /// <summary>Where a 2D composite's part reads: up, left, down, right (W A S D); anything else after them.</summary>
        public static int WayOrder(string? part)
        {
            for (int i = 0; i < VectorOrder.Length; i++)
            {
                if (string.Equals(VectorOrder[i], part, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return VectorOrder.Length;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            InputSystem.onActionChange -= OnActionChange;
            _deviceChanged.Dispose();
            _changed.Dispose();
        }

        private IReadOnlyList<KeyName> Parts(InputBinding composite, ReadOnlyArray<InputBinding> bindings, int from, int to,
            string? part)
        {
            var keys = new List<KeyName>(to - from);
            for (int i = from; i < to; i++)
            {
                InputBinding binding = bindings[i];
                if ((part == null || string.Equals(binding.name, part, StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrEmpty(binding.effectivePath))
                {
                    keys.Add(Name(binding.effectivePath).WithPart(binding.name));
                }
            }

            if (string.Equals(composite.GetNameOfComposite(), "2DVector", StringComparison.OrdinalIgnoreCase))
            {
                keys.Sort((a, b) => WayOrder(a.Part).CompareTo(WayOrder(b.Part)));
            }

            return keys;
        }

        private static bool IsWay(string part) => WayOrder(part) < VectorOrder.Length;

        private void OnActionChange(object changed, InputActionChange change)
        {
            if (change == InputActionChange.BoundControlsChanged && InputMaps.Touches(_maps, changed))
            {
                _changed.OnNext(Unit.Default);
            }
        }
    }
}
