using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Rubickanov.Input
{
    /// <summary>
    /// Devices the game feeds from code, for input Unity's Input System cannot see: the pads of Steam Remote Play Together
    /// guests, read through ISteamInput, and a guest's own keyboard and mouse, read through ISteamRemotePlay. Each is an
    /// ordinary Input System device, so actions, <see cref="ActiveDevice"/> and <see cref="LocalPlayers{TInput}"/> take it
    /// like any other; what it holds arrives as a state event, seen from the next input update. The devices go when this
    /// does. One per app.
    /// </summary>
    public sealed class FedDevices : IDisposable
    {
        private readonly List<InputDevice> _devices = new();
        private readonly Dictionary<Keyboard, KeyboardState> _keys = new();
        private readonly Dictionary<Mouse, MouseState> _mice = new();
        private bool _disposed;

        /// <summary>Every device fed now, in the order added.</summary>
        public IReadOnlyList<InputDevice> Devices => _devices;

        /// <summary>A pad of the Xbox layout; an <see cref="IPadFamilySource"/> tells the guest's real family.</summary>
        public Gamepad AddPad(string name) => Add<Gamepad>(name);

        public Keyboard AddKeyboard(string name)
        {
            var keyboard = Add<Keyboard>(name);
            _keys[keyboard] = default;
            return keyboard;
        }

        public Mouse AddMouse(string name)
        {
            var mouse = Add<Mouse>(name);
            _mice[mouse] = default;
            return mouse;
        }

        /// <summary>Whether <paramref name="device"/> is one of these.</summary>
        public bool IsFed(InputDevice device) => device != null && _devices.Contains(device);

        /// <summary>Everything <paramref name="pad"/> holds now: buttons, sticks, triggers.</summary>
        public void Set(Gamepad pad, in GamepadState state)
        {
            Require(pad);
            InputSystem.QueueStateEvent(pad, state);
        }

        /// <summary><paramref name="key"/> going down or up on <paramref name="keyboard"/>; the other keys stay.</summary>
        public void SetKey(Keyboard keyboard, Key key, bool down)
        {
            Require(keyboard);
            KeyboardState state = _keys[keyboard];
            state.Set(key, down);
            _keys[keyboard] = state;
            InputSystem.QueueStateEvent(keyboard, state);
        }

        /// <summary>Every key of <paramref name="keyboard"/> up: a guest who left, a window that lost focus.</summary>
        public void ReleaseKeys(Keyboard keyboard)
        {
            Require(keyboard);
            _keys[keyboard] = default;
            InputSystem.QueueStateEvent(keyboard, default(KeyboardState));
        }

        /// <summary>The mouse moved by <paramref name="delta"/> pixels; deltas within one update add up.</summary>
        public void Move(Mouse mouse, Vector2 delta)
        {
            MouseState state = Require(mouse);
            state.delta = delta;
            Send(mouse, state);
        }

        /// <summary>Where the pointer is, in pixels from the bottom left of the window.</summary>
        public void SetPosition(Mouse mouse, Vector2 position)
        {
            MouseState state = Require(mouse);
            state.position = position;
            Send(mouse, state);
        }

        public void SetButton(Mouse mouse, MouseButton button, bool down)
        {
            MouseState state = Require(mouse).WithButton(button, down);
            Send(mouse, state);
        }

        /// <summary>
        /// The wheel turned by <paramref name="scroll"/>, as is: Unity's scroll normalisation is native and skips fed
        /// devices, so feed 1 a notch to read like the host's mouse under the default ScrollDeltaBehavior.
        /// </summary>
        public void Scroll(Mouse mouse, Vector2 scroll)
        {
            MouseState state = Require(mouse);
            state.scroll = scroll;
            Send(mouse, state);
        }

        /// <summary>Takes <paramref name="device"/> out of the Input System: its guest left.</summary>
        public void Remove(InputDevice device)
        {
            if (!_devices.Remove(device))
            {
                return;
            }

            if (device is Keyboard keyboard)
            {
                _keys.Remove(keyboard);
            }
            else if (device is Mouse mouse)
            {
                _mice.Remove(mouse);
            }

            if (device.added)
            {
                InputSystem.RemoveDevice(device);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (int i = _devices.Count - 1; i >= 0; i--)
            {
                Remove(_devices[i]);
            }
        }

        private T Add<T>(string name) where T : InputDevice
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FedDevices));
            }

            // Adding makes a device current: the player's own keyboard, pad or mouse stays current until a guest's is used.
            InputDevice? current = typeof(T) == typeof(Gamepad) ? Gamepad.current
                : typeof(T) == typeof(Keyboard) ? Keyboard.current
                : typeof(T) == typeof(Mouse) ? Mouse.current
                : null;
            // A name is part of every control's path: a slash would split it.
            var device = InputSystem.AddDevice<T>(string.IsNullOrEmpty(name) ? null : name.Replace('/', '-'));
            _devices.Add(device);
            current?.MakeCurrent();
            return device;
        }

        private void Require(InputDevice device)
        {
            if (!IsFed(device))
            {
                throw new ArgumentException($"{device?.name ?? "null"} is not a fed device", nameof(device));
            }
        }

        private MouseState Require(Mouse mouse)
        {
            Require((InputDevice)mouse);
            return _mice[mouse];
        }

        // Motion and the wheel are this event's, not held: they go back to zero once sent, as a mouse's do.
        private void Send(Mouse mouse, MouseState state)
        {
            InputSystem.QueueStateEvent(mouse, state);
            state.delta = default;
            state.scroll = default;
            _mice[mouse] = state;
        }
    }
}
