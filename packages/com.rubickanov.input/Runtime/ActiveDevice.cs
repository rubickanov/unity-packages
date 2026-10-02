using System;
using System.Collections.Generic;
using R3;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace Rubickanov.Input
{
    /// <summary>
    /// Knows better than the pad's layout whose pad it is: Steam hands every pad over as an Xbox one, and a Steam Deck's
    /// own controls are a pad Unity calls Xbox too.
    /// </summary>
    public interface IPadFamilySource
    {
        /// <summary>The family of <paramref name="pad"/>; null to go by its layout.</summary>
        PadFamily? FamilyOf(Gamepad pad);
    }

    /// <summary>
    /// The device the player is on: keyboard and mouse, or a pad and which one. Any key, mouse button or wheel turns it to
    /// the keys, any pad control past <see cref="Actuation"/> to that pad; the mouse moving does not count, the mouse on a
    /// menu item does (<see cref="Pointed"/>). Watches every device's input, not an action map, so it follows the player
    /// whatever maps are on. A pad that presses together with the pad in hand is one pad seen twice (Steam Input's Xbox
    /// copy of the pad it reads, beside the pad itself) and never takes the hand. One per app.
    /// </summary>
    public sealed class ActiveDevice : IDisposable
    {
        /// <summary>How far a pad control goes before it counts: the sticks' drift does not.</summary>
        public const float Actuation = 0.5f;

        /// <summary>Two pads pressing this close in seconds press as one: Steam's copy lags the pad by a frame or so.</summary>
        public const double Together = 0.2;

        private readonly List<IPadFamilySource> _families = new();
        private readonly Subject<Unit> _changed = new();
        private readonly List<Gamepad> _echoes = new();
        private readonly Dictionary<InputDevice, PadFamily> _layoutFamilies = new();
        private InputDevice? _last;
        private Gamepad? _pad;
        private double _padAt = double.NegativeInfinity;
        private PadFamily _family;
        private PadFamily? _forced;
        private bool _disposed;

        /// <param name="families">
        /// Asked in turn for a pad's family before its layout; none or null goes by the layout alone. A container hands
        /// every registered source, or an empty list.
        /// </param>
        public ActiveDevice(IEnumerable<IPadFamilySource>? families = null)
        {
            if (families != null)
            {
                foreach (IPadFamilySource source in families)
                {
                    if (source != null)
                    {
                        _families.Add(source);
                    }
                }
            }

            InputSystem.onEvent += OnEvent;
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        public ControlScheme Scheme { get; private set; } = ControlScheme.Keys;

        /// <summary>The pad in hand, else the last one connected; null without a pad.</summary>
        public Gamepad? Pad => _pad ?? Gamepad.current;

        /// <summary>Whose buttons <see cref="Pad"/> has.</summary>
        public PadFamily Family => _forced ?? (_pad != null ? _family : FamilyOf(Gamepad.current));

        /// <summary>The family <see cref="Force"/> holds; null while the device follows the player.</summary>
        public PadFamily? Forced => _forced;

        /// <summary>What <see cref="Pad"/> is, for a console; null without a pad.</summary>
        public string? PadName => Pad?.displayName;

        /// <summary>What the pads that press as the pad in hand does (Steam's copies) are, for a console.</summary>
        public IReadOnlyList<string> EchoNames
        {
            get
            {
                var names = new string[_echoes.Count];
                for (int i = 0; i < names.Length; i++)
                {
                    names[i] = _echoes[i].displayName;
                }

                return names;
            }
        }

        /// <summary>The keyboard layout the system has on, for a console; null without a keyboard.</summary>
        public string? KeyboardLayout => Keyboard.current?.keyboardLayout;

        /// <summary>The scheme, the pad or its family changed, or the player switched keyboard layouts.</summary>
        public Observable<Unit> Changed => _changed;

        /// <summary>The mouse is on a menu item: the keys.</summary>
        public void Pointed()
        {
            if (_forced == null)
            {
                UseKeys(null);
            }
        }

        /// <summary>
        /// Holds the pad scheme with <paramref name="family"/>'s buttons whatever the player touches, to see every family's
        /// names and pictures without its pad; null follows the player again, from the keys.
        /// </summary>
        public void Force(PadFamily? family)
        {
            if (_forced == family)
            {
                return;
            }

            _forced = family;
            _last = null;
            Scheme = family != null ? ControlScheme.Pad : ControlScheme.Keys;
            _changed.OnNext(Unit.Default);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            InputSystem.onEvent -= OnEvent;
            InputSystem.onDeviceChange -= OnDeviceChange;
            _changed.Dispose();
        }

        // Called for every event of every device: no allocation here.
        private void OnEvent(InputEventPtr eventPtr, InputDevice device)
        {
            // Only state carries presses; the other events (a device's configuration, text) cannot be enumerated. A pad
            // is read even in hand, for when it last pressed.
            if (_forced != null || (device == _last && device is not Gamepad) ||
                device is not (Keyboard or Mouse or Gamepad) ||
                !(eventPtr.IsA<StateEvent>() || eventPtr.IsA<DeltaStateEvent>()))
            {
                return;
            }

            foreach (InputControl control in eventPtr.EnumerateChangedControls(device, Actuation))
            {
                if (device is Mouse mouse && control is not ButtonControl && control.parent != mouse.scroll)
                {
                    continue;
                }

                if (device is Gamepad pad)
                {
                    UsePad(pad, eventPtr.time);
                }
                else
                {
                    UseKeys(device);
                }

                return;
            }
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change is InputDeviceChange.Removed or InputDeviceChange.ConfigurationChanged)
            {
                _layoutFamilies.Remove(device);
            }

            if (change is InputDeviceChange.Removed or InputDeviceChange.Disconnected && device is Gamepad)
            {
                // A pad and its copy go together; which of those left is whose is learnt again.
                _echoes.Clear();
            }

            switch (change)
            {
                case InputDeviceChange.Removed or InputDeviceChange.Disconnected when device == _pad:
                    _pad = null;
                    _last = null;
                    _padAt = double.NegativeInfinity;
                    Scheme = _forced != null ? ControlScheme.Pad : ControlScheme.Keys;
                    _changed.OnNext(Unit.Default);
                    break;
                case InputDeviceChange.ConfigurationChanged when device is Keyboard:
                    _changed.OnNext(Unit.Default);
                    break;
            }
        }

        private void UsePad(Gamepad pad, double time)
        {
            if (_pad != null && pad != _pad)
            {
                if (_echoes.Contains(pad))
                {
                    return;
                }

                if (time - _padAt <= Together)
                {
                    Gamepad echo = EchoOf(_pad, pad);
                    _echoes.Add(echo);
                    if (echo == pad)
                    {
                        return;
                    }
                }
            }

            _last = pad;
            _padAt = time;
            PadFamily family = FamilyOf(pad);
            if (Scheme == ControlScheme.Pad && _pad == pad && _family == family)
            {
                return;
            }

            _pad = pad;
            _family = family;
            Scheme = ControlScheme.Pad;
            _changed.OnNext(Unit.Default);
        }

        private void UseKeys(InputDevice? device)
        {
            _last = device;
            if (Scheme == ControlScheme.Keys)
            {
                return;
            }

            Scheme = ControlScheme.Keys;
            _changed.OnNext(Unit.Default);
        }

        // Steam hands its copy over as an Xbox pad, so of two pads pressing as one an Xbox pad beside another family's is
        // the copy; of two alike the one in hand stays.
        private Gamepad EchoOf(Gamepad held, Gamepad other) =>
            FamilyOf(held) == PadFamily.Xbox && FamilyOf(other) != PadFamily.Xbox ? held : other;

        private PadFamily FamilyOf(Gamepad? pad)
        {
            if (pad == null)
            {
                return PadFamily.Xbox;
            }

            for (int i = 0; i < _families.Count; i++)
            {
                PadFamily? family = _families[i].FamilyOf(pad);
                if (family != null)
                {
                    return family.Value;
                }
            }

            return LayoutFamilyOf(pad);
        }

        // A pad sends events by the hundred a second and its layout compares by strings: asked once per pad.
        private PadFamily LayoutFamilyOf(Gamepad pad)
        {
            if (!_layoutFamilies.TryGetValue(pad, out PadFamily family))
            {
                family = KeyNames.FamilyOf(pad);
                _layoutFamilies[pad] = family;
            }

            return family;
        }
    }
}
