using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace Rubickanov.Input
{
    /// <summary>
    /// One key the player may choose: a binding of a rebindable action under one scheme, a composite's part on its own
    /// (Move's up). <see cref="Id"/> names it in the saved keys and a console: jump, move.up, jump.pad; a second binding
    /// of one scheme gets .2.
    /// </summary>
    public sealed class RebindSlot
    {
        public RebindSlot(string id, InputAction action, int bindingIndex, ControlScheme scheme, string? part)
        {
            Id = id;
            Action = action;
            BindingIndex = bindingIndex;
            Scheme = scheme;
            Part = part;
        }

        public string Id { get; }
        public InputAction Action { get; }
        public int BindingIndex { get; }
        public ControlScheme Scheme { get; }

        /// <summary>The composite part ("up"); null for a plain binding.</summary>
        public string? Part { get; }

        public InputBinding Binding => Action.bindings[BindingIndex];

        public override string ToString() => Id;
    }

    public enum RebindKind
    {
        /// <summary>The key was free: the slot has it now.</summary>
        Bound,

        /// <summary>
        /// Another slot has the key (<see cref="RebindResult.Other"/>); nothing changed. The player is asked, and a yes
        /// is <see cref="ControlBindings.Swap"/>.
        /// </summary>
        Taken,

        /// <summary>The slot already has the key.</summary>
        Unchanged,

        /// <summary>Not a key this slot takes: another scheme's device, the cancel keys, the OS keys, the mouse's movement.</summary>
        Refused,

        /// <summary>The player backed out (Esc, Start) or the listening was called off.</summary>
        Cancelled,
    }

    /// <summary>What came of choosing <see cref="Path"/> for <see cref="Slot"/>.</summary>
    public readonly struct RebindResult
    {
        public readonly RebindKind Kind;
        public readonly RebindSlot Slot;

        /// <summary>The key chosen, a binding path ("&lt;Keyboard&gt;/f"); null when cancelled.</summary>
        public readonly string? Path;

        /// <summary>Taken: the slot that has the key. Bound by a swap: the slot that took this slot's old key.</summary>
        public readonly RebindSlot? Other;

        public RebindResult(RebindKind kind, RebindSlot slot, string? path, RebindSlot? other = null)
        {
            Kind = kind;
            Slot = slot;
            Path = path;
            Other = other;
        }

        public override string ToString() => Other != null ? $"{Kind} {Slot} {Path} ({Other})" : $"{Kind} {Slot} {Path}";
    }

    /// <summary>
    /// The player's own keys for the actions <see cref="RebindOptions"/> names; every other binding stays fixed. A key
    /// another slot of the same scheme has comes back <see cref="RebindKind.Taken"/> and changes nothing until the player
    /// agrees to swap (<see cref="Swap"/>). The keys go out and come back as JSON (<see cref="ToJson"/>,
    /// <see cref="LoadJson"/>), only those that differ from the defaults, by slot id; <see cref="Changed"/> says when to
    /// keep them. One per input.
    /// </summary>
    public sealed class ControlBindings : IDisposable
    {
        private readonly IInputActionCollection2 _input;
        private readonly InputBlocker _blocker;
        private readonly RebindOptions _options;
        private readonly List<RebindSlot> _slots;
        private readonly Subject<Unit> _changed = new();
        private readonly CancellationTokenSource _life = new();

        /// <param name="blocker">Takes every map away while a key is listened for: Unity rebinds only disabled actions.</param>
        public ControlBindings(IInputActionCollection2 input, InputBlocker blocker, RebindOptions options)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _blocker = blocker ?? throw new ArgumentNullException(nameof(blocker));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _slots = BuildSlots(input, options);
        }

        /// <summary>Every key the player may choose, keyboard's first.</summary>
        public IReadOnlyList<RebindSlot> Slots => _slots;

        /// <summary>A key is being listened for (<see cref="ListenAsync"/>).</summary>
        public bool IsListening { get; private set; }

        /// <summary>The player's keys changed: a key bound or swapped, slots reset. Keep <see cref="ToJson"/> now.</summary>
        public Observable<Unit> Changed => _changed;

        /// <summary>The slot called <paramref name="id"/>, case aside; null for none.</summary>
        public RebindSlot? Find(string id)
        {
            foreach (RebindSlot slot in _slots)
            {
                if (string.Equals(slot.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return slot;
                }
            }

            return null;
        }

        /// <summary>The key <paramref name="slot"/> has now.</summary>
        public string Current(RebindSlot slot) => slot.Binding.effectivePath;

        /// <summary>The key <paramref name="slot"/> has by default.</summary>
        public string Default(RebindSlot slot) => slot.Binding.path;

        public bool IsDefault(RebindSlot slot) => Same(Current(slot), Default(slot));

        /// <summary>The other slot of <paramref name="slot"/>'s scheme that has <paramref name="path"/>; null for none.</summary>
        public RebindSlot? Holder(RebindSlot slot, string path)
        {
            foreach (RebindSlot other in _slots)
            {
                if (other != slot && other.Scheme == slot.Scheme && Same(Current(other), path))
                {
                    return other;
                }
            }

            return null;
        }

        /// <summary>Gives <paramref name="slot"/> the key <paramref name="path"/> if it is free, or says why not.</summary>
        public RebindResult Set(RebindSlot slot, string path)
        {
            RebindResult result = Check(slot, path);
            if (result.Kind == RebindKind.Bound)
            {
                Apply(slot, path);
                _changed.OnNext(Unit.Default);
            }

            return result;
        }

        /// <summary>
        /// The player agreed to <paramref name="taken"/>: its slot takes the key and the slot that had it takes the slot's
        /// old key, as one change. Comes back Bound with the other slot, or as <see cref="Set"/> would if the key has come
        /// free or moved on since.
        /// </summary>
        public RebindResult Swap(RebindResult taken)
        {
            RebindSlot slot = taken.Slot;
            RebindSlot? other = taken.Other;
            if (taken.Kind != RebindKind.Taken || other == null || taken.Path == null || !Same(Current(other), taken.Path))
            {
                return taken.Path != null ? Set(slot, taken.Path) : taken;
            }

            string old = Current(slot);
            Apply(other, old);
            Apply(slot, taken.Path);
            _changed.OnNext(Unit.Default);
            return new RebindResult(RebindKind.Bound, slot, taken.Path, other);
        }

        /// <summary>Gives <paramref name="slot"/> its default key back; Taken if another slot has it now.</summary>
        public RebindResult Reset(RebindSlot slot) => Set(slot, Default(slot));

        /// <summary>Every slot back to its default key.</summary>
        public void ResetAll()
        {
            foreach (RebindSlot slot in _slots)
            {
                slot.Action.RemoveBindingOverride(slot.BindingIndex);
            }

            _changed.OnNext(Unit.Default);
        }

        /// <summary>Every slot of <paramref name="scheme"/> back to its default key; the other scheme's keep theirs.</summary>
        public void ResetAll(ControlScheme scheme)
        {
            foreach (RebindSlot slot in _slots)
            {
                if (slot.Scheme == scheme)
                {
                    slot.Action.RemoveBindingOverride(slot.BindingIndex);
                }
            }

            _changed.OnNext(Unit.Default);
        }

        /// <summary>
        /// Waits for the player to press the key for <paramref name="slot"/>: a key or mouse button for a keyboard slot, a
        /// pad control for a pad slot. The cancel key or button backs out. Every map is blocked meanwhile, so nothing else
        /// hears the press. A free key is bound; a taken one comes back Taken for the player to agree to the swap. With
        /// the input's <c>devices</c> set (a local player's), only those devices are heard, the cancel key or button too,
        /// and a slot of a scheme the player has no device for comes back Cancelled at once.
        /// </summary>
        public UniTask<RebindResult> ListenAsync(RebindSlot slot, CancellationToken ct)
        {
            if (slot == null)
            {
                throw new ArgumentNullException(nameof(slot));
            }

            if (IsListening)
            {
                throw new InvalidOperationException("A key is already being listened for");
            }

            return ct.IsCancellationRequested || _life.IsCancellationRequested || !HasDeviceFor(slot.Scheme)
                ? UniTask.FromResult(new RebindResult(RebindKind.Cancelled, slot, null))
                : ListenCoreAsync(slot, ct);
        }

        /// <summary>The keys that differ from the defaults as JSON, by slot id: what to keep.</summary>
        public string ToJson()
        {
            var keys = new List<KeyValuePair<string, string>>();
            foreach (RebindSlot slot in _slots)
            {
                if (!IsDefault(slot))
                {
                    keys.Add(new KeyValuePair<string, string>(slot.Id, Current(slot)));
                }
            }

            return BindingsJson.Write(keys);
        }

        /// <summary>
        /// Takes the keys kept as <see cref="ToJson"/> wrote them: every slot the JSON leaves out has its default. Throws
        /// <see cref="FormatException"/>, changing nothing, when <paramref name="json"/> is not that: keep the text aside.
        /// A slot that is gone or a key the slot does not take is dropped with a warning; if two slots of a scheme end up
        /// on one key, that scheme goes back to its defaults, which never share. <paramref name="source"/> names where the
        /// text came from in the warnings. Not a <see cref="Changed"/>: the keys are as kept.
        /// </summary>
        public void LoadJson(string json, string source = "controls")
        {
            Dictionary<string, string> keys = BindingsJson.Read(json);
            foreach (RebindSlot slot in _slots)
            {
                slot.Action.RemoveBindingOverride(slot.BindingIndex);
            }

            foreach (KeyValuePair<string, string> key in keys)
            {
                RebindSlot? slot = Find(key.Key);
                if (slot == null)
                {
                    Debug.LogWarning($"[Input] {source}: no key is called {key.Key}; its {key.Value} is dropped");
                }
                else if (!Takes(slot, key.Value))
                {
                    Debug.LogWarning($"[Input] {source}: {key.Value} is not a key {slot.Id} takes; it keeps {Default(slot)}");
                }
                else
                {
                    Apply(slot, key.Value);
                }
            }

            ResetSchemesThatShare(source, ControlScheme.Keys);
            ResetSchemesThatShare(source, ControlScheme.Pad);
        }

        public void Dispose()
        {
            if (_life.IsCancellationRequested)
            {
                return;
            }

            _life.Cancel();
            _life.Dispose();
            _changed.Dispose();
        }

        private async UniTask<RebindResult> ListenCoreAsync(RebindSlot slot, CancellationToken ct)
        {
            IsListening = true;
            _blocker.Set(this, true, InputScope.All);
            var done = new UniTaskCompletionSource<RebindResult>();
            RebindResult chosen = default;
            InputActionRebindingExtensions.RebindingOperation? operation = null;
            try
            {
                operation = Listen(slot)
                    .OnApplyBinding((_, path) => chosen = Set(slot, path))
                    .OnComplete(_ => done.TrySetResult(chosen))
                    .OnCancel(_ => done.TrySetResult(new RebindResult(RebindKind.Cancelled, slot, null)));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _life.Token);
                // The operation hooks the input system and swallows what matches it until it is disposed.
                InputActionRebindingExtensions.RebindingOperation started = operation;
                using CancellationTokenRegistration stop = linked.Token.Register(() => started.Cancel());
                operation.Start();
                return await done.Task;
            }
            finally
            {
                operation?.Dispose();
                _blocker.Set(this, false);
                IsListening = false;
            }
        }

        private InputActionRebindingExtensions.RebindingOperation Listen(RebindSlot slot)
        {
            string cancelButton = _options.CancelButton;
            ReadOnlyArray<InputDevice>? devices = _input.devices;
            InputActionRebindingExtensions.RebindingOperation operation =
                (devices == null ? slot.Action.PerformInteractiveRebinding(slot.BindingIndex) : PlayersOperation(slot))
                .WithMagnitudeHavingToBeGreaterThan(_options.Actuation)
                .OnMatchWaitForAnother(_options.SettleTime)
                .OnPotentialMatch(op =>
                {
                    if (op.candidates.Count > 0 && InputControlPath.Matches(cancelButton, op.candidates[0]))
                    {
                        op.Cancel();
                    }
                });
            if (devices == null)
            {
                // The pad's cancel backs out whatever the slot; it has to be a candidate to be seen.
                operation.WithCancelingThrough(_options.CancelKey).WithControlsHavingToMatchPath(cancelButton);
                if (slot.Scheme == ControlScheme.Pad)
                {
                    operation.WithControlsHavingToMatchPath("<Gamepad>");
                }
                else
                {
                    operation.WithControlsHavingToMatchPath("<Keyboard>");
                    foreach (string button in _options.MouseButtons)
                    {
                        operation.WithControlsHavingToMatchPath(button);
                    }
                }
            }
            else
            {
                HearOnly(operation, slot.Scheme, devices.Value);
            }

            foreach (string excluded in slot.Scheme == ControlScheme.Pad ? _options.PadExcluded : _options.KeysExcluded)
            {
                operation.WithControlsExcluding(excluded);
            }

            return operation;
        }

        // PerformInteractiveRebinding without a target binding: a target binding in a control scheme lets in every device
        // the scheme names, every pad among them. The slot is applied by Set anyway; a composite's part still looks for
        // its part's kind of control.
        private static InputActionRebindingExtensions.RebindingOperation PlayersOperation(RebindSlot slot)
        {
            InputActionRebindingExtensions.RebindingOperation operation =
                new InputActionRebindingExtensions.RebindingOperation()
                    .WithAction(slot.Action)
                    .WithControlsExcluding("<Pointer>/delta")
                    .WithControlsExcluding("<Pointer>/position")
                    .WithControlsExcluding("<Touchscreen>/touch*/position")
                    .WithControlsExcluding("<Touchscreen>/touch*/delta")
                    .WithControlsExcluding("<Mouse>/clickCount")
                    .WithMatchingEventsBeingSuppressed();
            if (slot.Part != null)
            {
                string? layout = InputBindingComposite.GetExpectedControlLayoutName(CompositeOf(slot), slot.Part);
                if (!string.IsNullOrEmpty(layout))
                {
                    operation.WithExpectedControlType(layout);
                }
            }

            return operation;
        }

        private static string CompositeOf(RebindSlot slot)
        {
            ReadOnlyArray<InputBinding> bindings = slot.Action.bindings;
            for (int i = slot.BindingIndex; i >= 0; i--)
            {
                if (bindings[i].isComposite)
                {
                    return bindings[i].GetNameOfComposite();
                }
            }

            return "";
        }

        private bool HasDeviceFor(ControlScheme scheme)
        {
            ReadOnlyArray<InputDevice>? devices = _input.devices;
            if (devices == null)
            {
                return true;
            }

            foreach (InputDevice device in devices.Value)
            {
                if (scheme == ControlScheme.Pad ? device is Gamepad : device is Keyboard)
                {
                    return true;
                }
            }

            return false;
        }

        // The same paths as for every device, each made the player's own device's: another player's keys are no
        // candidates and their Esc or Start cancels nothing.
        private void HearOnly(InputActionRebindingExtensions.RebindingOperation operation, ControlScheme scheme,
            ReadOnlyArray<InputDevice> devices)
        {
            foreach (InputDevice device in devices)
            {
                switch (device)
                {
                    case Gamepad pad:
                        IncludeOn(operation, pad, _options.CancelButton);
                        if (scheme == ControlScheme.Pad)
                        {
                            operation.WithControlsHavingToMatchPath(pad.path);
                        }

                        break;
                    case Keyboard keyboard:
                        InputControl? cancel = InputControlPath.TryFindControl(keyboard, _options.CancelKey);
                        if (cancel != null)
                        {
                            operation.WithCancelingThrough(cancel);
                        }

                        if (scheme == ControlScheme.Keys)
                        {
                            operation.WithControlsHavingToMatchPath(keyboard.path);
                        }

                        break;
                    case Mouse mouse when scheme == ControlScheme.Keys:
                        foreach (string button in _options.MouseButtons)
                        {
                            IncludeOn(operation, mouse, button);
                        }

                        break;
                }
            }
        }

        private static void IncludeOn(InputActionRebindingExtensions.RebindingOperation operation, InputDevice device,
            string path)
        {
            InputControl? control = InputControlPath.TryFindControl(device, path);
            if (control != null)
            {
                operation.WithControlsHavingToMatchPath(control.path);
            }
        }

        private RebindResult Check(RebindSlot slot, string path)
        {
            if (!Takes(slot, path))
            {
                return new RebindResult(RebindKind.Refused, slot, path);
            }

            if (Same(Current(slot), path))
            {
                return new RebindResult(RebindKind.Unchanged, slot, path);
            }

            RebindSlot? other = Holder(slot, path);
            return other != null
                ? new RebindResult(RebindKind.Taken, slot, path, other)
                : new RebindResult(RebindKind.Bound, slot, path);
        }

        // A key of the slot's own devices that nothing else needs.
        private bool Takes(RebindSlot slot, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            InputControlPath.ToHumanReadableString(path, out string? layout, out string? control);
            if (string.IsNullOrEmpty(layout) || string.IsNullOrEmpty(control))
            {
                return false;
            }

            if (slot.Scheme == ControlScheme.Pad)
            {
                return InputSystem.IsFirstLayoutBasedOnSecond(layout, "Gamepad") && !Same(path, _options.CancelButton) &&
                       !Matches(_options.PadExcluded, path);
            }

            if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "Keyboard"))
            {
                return !Same(path, _options.CancelKey) && !Matches(_options.KeysExcluded, "<Keyboard>/" + control);
            }

            return InputSystem.IsFirstLayoutBasedOnSecond(layout, "Mouse") &&
                   Matches(_options.MouseButtons, "<Mouse>/" + control);
        }

        private static void Apply(RebindSlot slot, string path)
        {
            if (Same(path, slot.Binding.path))
            {
                slot.Action.RemoveBindingOverride(slot.BindingIndex);
            }
            else
            {
                slot.Action.ApplyBindingOverride(slot.BindingIndex, path);
            }
        }

        private void ResetSchemesThatShare(string source, ControlScheme scheme)
        {
            foreach (RebindSlot slot in _slots)
            {
                RebindSlot? other = slot.Scheme == scheme ? Holder(slot, Current(slot)) : null;
                if (other == null)
                {
                    continue;
                }

                Debug.LogWarning($"[Input] {source}: {slot.Id} and {other.Id} share {Current(slot)}; " +
                                 $"the {scheme} keys go back to the defaults");
                foreach (RebindSlot reset in _slots)
                {
                    if (reset.Scheme == scheme)
                    {
                        reset.Action.RemoveBindingOverride(reset.BindingIndex);
                    }
                }

                return;
            }
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // A path that is one of the patterns; a pattern ending in * takes any control below it.
        private static bool Matches(IReadOnlyList<string> patterns, string path)
        {
            foreach (string pattern in patterns)
            {
                if (pattern.EndsWith("*", StringComparison.Ordinal)
                        ? path.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase)
                        : Same(pattern, path))
                {
                    return true;
                }
            }

            return false;
        }

        // A stick or the d-pad bound whole moves its action; no button could stand in for it.
        private static bool IsWholeStick(string path) =>
            path.EndsWith("/leftStick", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/rightStick", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/dpad", StringComparison.OrdinalIgnoreCase);

        private static List<RebindSlot> BuildSlots(IInputActionCollection2 input, RebindOptions options)
        {
            var slots = new List<RebindSlot>();
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (ControlScheme scheme in new[] { ControlScheme.Keys, ControlScheme.Pad })
            {
                string? group = InputMaps.GroupOf(input, scheme);
                foreach (string name in options.Actions)
                {
                    InputAction action = input.FindAction(name, true);
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        InputBinding binding = action.bindings[i];
                        if (binding.isComposite || IsWholeStick(binding.path) || !IsOfScheme(binding, scheme, group))
                        {
                            continue;
                        }

                        string? part = binding.isPartOfComposite ? binding.name : null;
                        string id = action.name.ToLowerInvariant() + (part != null ? "." + part.ToLowerInvariant() : "") +
                                    (scheme == ControlScheme.Pad ? ".pad" : "");
                        int count = seen.TryGetValue(id, out int had) ? had + 1 : 1;
                        seen[id] = count;
                        slots.Add(new RebindSlot(count > 1 ? $"{id}.{count}" : id, action, i, scheme, part));
                    }
                }
            }

            return slots;
        }

        // By the default path, not the player's: a slot stays its scheme's whatever it is rebound to.
        private static bool IsOfScheme(InputBinding binding, ControlScheme scheme, string? group)
        {
            if (group != null && !string.IsNullOrEmpty(binding.groups))
            {
                return InputBinding.MaskByGroup(group).Matches(binding);
            }

            return !string.IsNullOrEmpty(binding.path) &&
                   InputMaps.SchemeOfLayout(InputControlPath.TryGetDeviceLayout(binding.path)) == scheme;
        }
    }
}
