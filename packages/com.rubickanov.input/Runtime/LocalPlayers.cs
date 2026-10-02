using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace Rubickanov.Input
{
    /// <summary>How players join a <see cref="LocalPlayers{TInput}"/>.</summary>
    public sealed class LocalPlayersOptions
    {
        public const int DefaultMaxPlayers = 4;

        /// <summary>How many seats there are; a press with every seat taken joins no one.</summary>
        public int MaxPlayers { get; set; } = DefaultMaxPlayers;

        /// <summary>Gives each player <see cref="LocalPlayer{TInput}.Bindings"/> over these actions, or none.</summary>
        public RebindOptions? Rebind { get; set; }

        /// <summary>Whether a press of this control on a free device joins; <see cref="IsJoinPress"/> by default.</summary>
        public Func<InputControl, bool> JoinsOn { get; set; } = IsJoinPress;

        /// <summary>
        /// A button every pad has (face buttons, start, select, shoulders, triggers, the d-pad's ways, stick presses), a
        /// key, a mouse button. Not a stick's drift or ways, the mouse moving or its wheel, and no family's own control (a
        /// DualShock's touchpad): two pads pressing as one are told apart by these.
        /// </summary>
        public static bool IsJoinPress(InputControl control) =>
            control is ButtonControl && !control.synthetic && control.device switch
            {
                Gamepad pad => IsEveryPads(pad, control),
                Mouse mouse => control.parent == mouse,
                Keyboard => true,
                _ => false,
            };

        private static bool IsEveryPads(Gamepad pad, InputControl control) =>
            control == pad.buttonSouth || control == pad.buttonEast || control == pad.buttonWest ||
            control == pad.buttonNorth || control == pad.startButton || control == pad.selectButton ||
            control == pad.leftShoulder || control == pad.rightShoulder || control == pad.leftTrigger ||
            control == pad.rightTrigger || control == pad.leftStickButton || control == pad.rightStickButton ||
            control.parent == pad.dpad;
    }

    /// <summary>
    /// One player at a shared screen: a copy of the game's input that hears only this player's devices, and the package's
    /// objects over it. Made and disposed by <see cref="LocalPlayers{TInput}"/>.
    /// </summary>
    public sealed class LocalPlayer<TInput> where TInput : class, IInputActionCollection2
    {
        private readonly List<InputDevice> _devices;

        internal LocalPlayer(int seat, TInput input, List<InputDevice> devices, IReadOnlyList<IPadFamilySource> families,
            RebindOptions? rebind, Dictionary<object, InputScope> blocks)
        {
            Seat = seat;
            Input = input;
            _devices = devices;
            Pair();
            Blocker = new InputBlocker(input);
            foreach (KeyValuePair<object, InputScope> block in blocks)
            {
                Blocker.Set(block.Key, true, block.Value);
            }

            Device = new ActiveDevice(families, Has);
            Device.Hold(devices[0]);
            Names = new ControlNames(input, Device);
            Bindings = rebind != null ? new ControlBindings(input, Blocker, rebind) : null;
        }

        /// <summary>The player's place, from 0: player 1 is seat 0. A seat left is the next one taken.</summary>
        public int Seat { get; }

        /// <summary>The player's own copy of the game's input, whose actions hear only <see cref="Devices"/>.</summary>
        public TInput Input { get; }

        /// <summary>The devices the player plays on: a pad, or a keyboard with a mouse.</summary>
        public IReadOnlyList<InputDevice> Devices => _devices;

        /// <summary>The player's device in hand, among <see cref="Devices"/>.</summary>
        public ActiveDevice Device { get; }

        /// <summary>The player's keys named for their device, for the player's hints.</summary>
        public ControlNames Names { get; }

        /// <summary>Blocks the player's maps; <see cref="LocalPlayers{TInput}.Block"/> blocks every player's.</summary>
        public InputBlocker Blocker { get; }

        /// <summary>The player's own keys, with <see cref="LocalPlayersOptions.Rebind"/>; null without.</summary>
        public ControlBindings? Bindings { get; }

        /// <summary>The player's pad or keyboard went away: pause for them until it, or another, comes back.</summary>
        public bool IsLost { get; internal set; }

        public bool Has(InputDevice device) => _devices.Contains(device);

        public override string ToString() => $"P{Seat + 1}";

        internal List<InputDevice> DeviceList => _devices;

        internal void Pair() => Input.devices = _devices.ToArray();

        internal void Dispose()
        {
            Bindings?.Dispose();
            Names.Dispose();
            Device.Dispose();
            Blocker.Dispose();
            switch (Input)
            {
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
                case InputActionAsset asset when Application.isPlaying:
                    Object.Destroy(asset);
                    break;
                case InputActionAsset asset:
                    Object.DestroyImmediate(asset);
                    break;
            }
        }
    }

    /// <summary>
    /// Players sharing one machine: couch co-op, and Steam Remote Play Together, whose guests are local players to the
    /// game (their pads through <see cref="FedDevices"/>). Each player gets a copy of the game's input from the factory,
    /// its actions limited to the player's devices (<c>devices</c> on the input, not Unity's InputUser), with their own
    /// <see cref="ActiveDevice"/>, <see cref="ControlNames"/>, <see cref="InputBlocker"/> and, if asked,
    /// <see cref="ControlBindings"/>. While <see cref="Joining"/>, a button on a device no one has seats a new player: a
    /// pad alone, a keyboard with a free mouse. A player whose pad or keyboard goes is lost; the same device coming back,
    /// or a press on a free one of its kind, seats them again, joining or not.
    /// <para>
    /// Steam Input shows a pad twice, the pad and its Xbox copy, pressing as one a little after it. A pad's press waits
    /// <see cref="ActiveDevice.Together"/> before it seats anyone, and a press of a control another pad pressed just
    /// before seats no one, and a free Xbox pad pressing just before a pad of another family, seated or not, is taken
    /// for that pad's copy. So a copy never takes a seat of its own, and a newcomer pressing the same button just after
    /// another player, or on an Xbox pad just before a player on another family's, joins on their next press. Only
    /// buttons every pad has join (<see cref="LocalPlayersOptions.IsJoinPress"/>).
    /// </para>
    /// </summary>
    public sealed class LocalPlayers<TInput> : IDisposable where TInput : class, IInputActionCollection2
    {
        private readonly Func<TInput> _create;
        private readonly LocalPlayersOptions _options;
        private readonly List<IPadFamilySource> _families = new();
        private readonly List<LocalPlayer<TInput>> _players = new();
        private readonly Dictionary<object, InputScope> _blocks = new();
        private readonly Dictionary<Gamepad, Press> _padPresses = new();
        private readonly List<Press> _pending = new();
        private readonly HashSet<InputDevice> _gone = new();
        private readonly Subject<LocalPlayer<TInput>> _joined = new();
        private readonly Subject<LocalPlayer<TInput>> _left = new();
        private readonly Subject<LocalPlayer<TInput>> _lost = new();
        private readonly Subject<LocalPlayer<TInput>> _regained = new();
        private bool _disposed;

        /// <param name="create">A fresh copy of the game's input on every call: <c>() => new GameInput()</c>.</param>
        /// <param name="families">Asked for a pad's family before its layout, for every player.</param>
        public LocalPlayers(Func<TInput> create, LocalPlayersOptions? options = null,
            IEnumerable<IPadFamilySource>? families = null)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
            _options = options ?? new LocalPlayersOptions();
            if (_options.MaxPlayers < 1)
            {
                throw new ArgumentException("A game has a seat at least", nameof(options));
            }

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
            InputSystem.onAfterUpdate += OnAfterUpdate;
        }

        /// <summary>The players in seat order.</summary>
        public IReadOnlyList<LocalPlayer<TInput>> Players => _players;

        /// <summary>A button on a free device seats a new player. Off by default: a lobby turns it on.</summary>
        public bool Joining { get; set; }

        public int MaxPlayers => _options.MaxPlayers;

        public Observable<LocalPlayer<TInput>> Joined => _joined;

        /// <summary>A player left (<see cref="Leave"/>); their input is disposed by now.</summary>
        public Observable<LocalPlayer<TInput>> Left => _left;

        /// <summary>A player's pad or keyboard went away.</summary>
        public Observable<LocalPlayer<TInput>> Lost => _lost;

        /// <summary>A lost player has a device again: the same one back, or a free one pressed.</summary>
        public Observable<LocalPlayer<TInput>> Regained => _regained;

        /// <summary>The player who has <paramref name="device"/>; null when no one does.</summary>
        public LocalPlayer<TInput>? OwnerOf(InputDevice device)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].Has(device))
                {
                    return _players[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Seats a player on exactly <paramref name="devices"/> (a guest's own keyboard and mouse), joining or not; null
        /// with every seat taken. Throws for a device someone has.
        /// </summary>
        public LocalPlayer<TInput>? Join(params InputDevice[] devices)
        {
            if (devices == null || devices.Length == 0)
            {
                throw new ArgumentException("Name the player's devices", nameof(devices));
            }

            foreach (InputDevice device in devices)
            {
                if (device == null)
                {
                    throw new ArgumentNullException(nameof(devices));
                }

                if (OwnerOf(device) != null)
                {
                    throw new InvalidOperationException($"{device.name} is {OwnerOf(device)}'s");
                }
            }

            return Seat(new List<InputDevice>(devices));
        }

        /// <summary>Frees <paramref name="player"/>'s seat and devices and disposes their input.</summary>
        public void Leave(LocalPlayer<TInput> player)
        {
            if (player == null || !_players.Remove(player))
            {
                return;
            }

            player.Dispose();
            _left.OnNext(player);
        }

        /// <summary>
        /// Blocks <paramref name="scope"/>'s maps of every player, those who join later too, under
        /// <paramref name="reason"/>, or lifts it: a pause or a console that covers everyone.
        /// </summary>
        public void Block(object reason, bool blocked, InputScope? scope = null)
        {
            if (reason == null)
            {
                throw new ArgumentNullException(nameof(reason));
            }

            if (blocked)
            {
                _blocks[reason] = scope ?? InputScope.All;
            }
            else
            {
                _blocks.Remove(reason);
            }

            foreach (LocalPlayer<TInput> player in _players)
            {
                player.Blocker.Set(reason, blocked, scope);
            }
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
            InputSystem.onAfterUpdate -= OnAfterUpdate;
            foreach (LocalPlayer<TInput> player in _players)
            {
                player.Dispose();
            }

            _players.Clear();
            _joined.Dispose();
            _left.Dispose();
            _lost.Dispose();
            _regained.Dispose();
        }

        // Called for every event of every device: no allocation here.
        private void OnEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (device is not (Gamepad or Keyboard or Mouse) ||
                !(eventPtr.IsA<StateEvent>() || eventPtr.IsA<DeltaStateEvent>()))
            {
                return;
            }

            InputControl? pressed = null;
            foreach (InputControl control in eventPtr.EnumerateChangedControls(device, ActiveDevice.Actuation))
            {
                if (_options.JoinsOn(control))
                {
                    pressed = control;
                    break;
                }
            }

            if (pressed == null)
            {
                return;
            }

            var press = new Press(device, pressed, eventPtr.time);
            bool follows = false;
            if (device is Gamepad pad)
            {
                follows = Follows(pad, press);
                _padPresses[pad] = press;
            }

            if (!follows && OwnerOf(device) == null && !IsPending(device) && CouldSeat(device))
            {
                _pending.Add(press);
            }
        }

        // Another pad pressed the same control just before: this press may be its copy, Steam's lagging the pad it reads,
        // and does not join. Only by family is the earlier one the copy: a pad of another family pressing after a free
        // Xbox pad is the pad, and the Xbox one, Steam's copy's layout, waits no more, the later pad seated or not.
        private bool Follows(Gamepad pad, Press press)
        {
            bool owned = OwnerOf(pad) != null;
            bool follows = false;
            foreach (KeyValuePair<Gamepad, Press> other in _padPresses)
            {
                if (other.Key == pad || other.Value.At > press.At || !other.Value.IsAlong(press))
                {
                    continue;
                }

                if (OwnerOf(other.Key) == null && Leads(pad, other.Key))
                {
                    Unpend(other.Key);
                }
                else if (!owned)
                {
                    follows = true;
                }
            }

            return follows;
        }

        private static bool Leads(Gamepad later, Gamepad earlier) =>
            KeyNames.FamilyOf(earlier) == PadFamily.Xbox && KeyNames.FamilyOf(later) != PadFamily.Xbox;

        private void Unpend(InputDevice device)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Device == device)
                {
                    _pending.RemoveAt(i);
                }
            }
        }

        // A pad waits out Together for a copy pressing after it; the keys have no copies and seat at the next update.
        private void OnAfterUpdate()
        {
            double now = InputState.currentTime;
            for (int i = 0; i < _pending.Count; i++)
            {
                Press press = _pending[i];
                if (press.Device is Gamepad && now - press.At <= ActiveDevice.Together)
                {
                    continue;
                }

                _pending.RemoveAt(i--);
                if (press.Device.added && !_gone.Contains(press.Device) && OwnerOf(press.Device) == null)
                {
                    SeatOnPress(press.Device);
                }
            }
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            switch (change)
            {
                case InputDeviceChange.Removed or InputDeviceChange.Disconnected:
                    _gone.Add(device);
                    if (device is Gamepad pad)
                    {
                        _padPresses.Remove(pad);
                    }

                    Unpend(device);
                    LocalPlayer<TInput>? owner = OwnerOf(device);
                    if (owner is { IsLost: false } && !CanPlay(owner))
                    {
                        owner.IsLost = true;
                        _lost.OnNext(owner);
                    }

                    break;
                case InputDeviceChange.Added or InputDeviceChange.Reconnected:
                    _gone.Remove(device);
                    LocalPlayer<TInput>? back = OwnerOf(device);
                    if (back == null)
                    {
                        break;
                    }

                    // Unity drops a removed device from the actions' devices: any of the player's coming back is paired
                    // again, a mouse beside a keyboard that stayed too.
                    back.Pair();
                    if (back.IsLost && CanPlay(back))
                    {
                        back.IsLost = false;
                        back.Device.Hold(device);
                        _regained.OnNext(back);
                    }

                    break;
            }
        }

        private void SeatOnPress(InputDevice device)
        {
            LocalPlayer<TInput>? lost = LostPlayerFor(device);
            if (lost != null)
            {
                Reseat(lost, device);
                return;
            }

            if (!Joining || _players.Count >= _options.MaxPlayers)
            {
                return;
            }

            // A mouse alone is no way to play: it joins with a free keyboard or not at all.
            InputDevice? half = FreeHalf(device);
            if (device is Mouse && half == null)
            {
                return;
            }

            var devices = new List<InputDevice> { device };
            if (half != null)
            {
                devices.Add(half);
            }

            Seat(devices);
        }

        private LocalPlayer<TInput>? Seat(List<InputDevice> devices)
        {
            if (_players.Count >= _options.MaxPlayers)
            {
                return null;
            }

            int seat = 0;
            int at = 0;
            for (; at < _players.Count && _players[at].Seat == seat; at++)
            {
                seat++;
            }

            TInput input = _create() ?? throw new InvalidOperationException("The input factory gave null");
            var player = new LocalPlayer<TInput>(seat, input, devices, _families, _options.Rebind, _blocks);
            _players.Insert(at, player);
            _joined.OnNext(player);
            return player;
        }

        // The lost device's kind goes, the new one comes; a keyboard brings a free mouse if the player's went too. Only a
        // pad for a lost pad or a keyboard for a lost keyboard comes here (LostPlayerFor), so the player plays again.
        private void Reseat(LocalPlayer<TInput> player, InputDevice device)
        {
            List<InputDevice> devices = player.DeviceList;
            bool pad = device is Gamepad;
            for (int i = devices.Count - 1; i >= 0; i--)
            {
                if (_gone.Contains(devices[i]) && (devices[i] is Gamepad) == pad)
                {
                    devices.RemoveAt(i);
                }
            }

            devices.Add(device);
            InputDevice? half = pad ? null : FreeHalf(device);
            if (half != null && !HasKind(devices, half))
            {
                devices.Add(half);
            }

            player.Pair();
            player.IsLost = false;
            player.Device.Hold(device);
            _regained.OnNext(player);
        }

        private LocalPlayer<TInput>? LostPlayerFor(InputDevice device)
        {
            foreach (LocalPlayer<TInput> player in _players)
            {
                if (!player.IsLost)
                {
                    continue;
                }

                foreach (InputDevice had in player.Devices)
                {
                    if (_gone.Contains(had) &&
                        (had is Gamepad && device is Gamepad || had is Keyboard && device is Keyboard))
                    {
                        return player;
                    }
                }
            }

            return null;
        }

        private bool CouldSeat(InputDevice device) =>
            (Joining && _players.Count < _options.MaxPlayers) || LostPlayerFor(device) != null;

        // A pad or a keyboard to play on; a mouse alone does not do.
        private bool CanPlay(LocalPlayer<TInput> player)
        {
            foreach (InputDevice device in player.Devices)
            {
                if (device is Gamepad or Keyboard && !_gone.Contains(device))
                {
                    return true;
                }
            }

            return false;
        }

        // The keys come as a pair: a free mouse for a keyboard, a free keyboard for a mouse.
        private InputDevice? FreeHalf(InputDevice device)
        {
            if (device is Gamepad)
            {
                return null;
            }

            foreach (InputDevice other in InputSystem.devices)
            {
                if ((device is Keyboard ? other is Mouse : other is Keyboard) && !_gone.Contains(other) &&
                    OwnerOf(other) == null)
                {
                    return other;
                }
            }

            return null;
        }

        private static bool HasKind(List<InputDevice> devices, InputDevice like)
        {
            foreach (InputDevice device in devices)
            {
                if (like is Mouse ? device is Mouse : device is Keyboard)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsPending(InputDevice device)
        {
            foreach (Press press in _pending)
            {
                if (press.Device == device)
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct Press
        {
            public readonly InputDevice Device;
            public readonly double At;
            private readonly string _control;
            private readonly string? _parent;

            public Press(InputDevice device, InputControl control, double at)
            {
                Device = device;
                At = at;
                _control = control.name;
                _parent = control.parent != device ? control.parent.name : null;
            }

            // The same control of another pad's layout (buttonSouth, dpad's up) within Together.
            public bool IsAlong(Press other) =>
                Math.Abs(other.At - At) <= ActiveDevice.Together && _control == other._control && _parent == other._parent;
        }
    }
}
