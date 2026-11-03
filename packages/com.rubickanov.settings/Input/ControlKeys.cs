using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using Rubickanov.Input;
using UnityEngine.InputSystem;

namespace Rubickanov.Settings.Input
{
    /// <summary>
    /// The player's keys as settings rows over <see cref="ControlBindings"/>: a heading (KEYS or BUTTONS) and a row per
    /// rebindable action of a scheme, its ways in reading order (W A S D), named by <see cref="ControlNames"/>. The rows of
    /// a scheme are made once and kept, so a screen made again keeps their views and picked keys.
    /// </summary>
    public sealed class ControlKeys : IDisposable
    {
        public const string KeysHeading = "keys.heading.keys";
        public const string PadHeading = "keys.heading.pad";

        private readonly Dictionary<ControlScheme, SettingsRow[]> _rows = new();
        private readonly Subject<Unit> _changed = new();
        private readonly IDisposable _subscriptions;
        private readonly string _submit;
        private bool _disposed;

        /// <param name="submit">The action that starts the listening ("UI/Submit"), named in a key row's line.</param>
        public ControlKeys(ControlBindings bindings, ControlNames names, ActiveDevice device, string submit = "UI/Submit")
        {
            Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            Names = names ?? throw new ArgumentNullException(nameof(names));
            Device = device ?? throw new ArgumentNullException(nameof(device));
            _submit = submit;
            _subscriptions = Disposable.Combine(
                names.Changed.Subscribe(_ => _changed.OnNext(Unit.Default)),
                bindings.Changed.Subscribe(_ => _changed.OnNext(Unit.Default)));
        }

        public ControlBindings Bindings { get; }

        public ControlNames Names { get; }

        public ActiveDevice Device { get; }

        /// <summary>The scheme of the device in hand.</summary>
        public ControlScheme InHand => Device.Scheme;

        /// <summary>The device in hand switched, a key was renamed or rebound: a screen makes its key rows again.</summary>
        public Observable<Unit> Changed => _changed;

        /// <summary>A row's id and the text key of its label: <c>keys.Player/Jump</c>.</summary>
        public static string LabelKey(InputAction action) =>
            "keys." + (action.actionMap != null ? action.actionMap.name + "/" : "") + action.name;

        /// <summary>The heading of <paramref name="scheme"/> and a row per action with keys of it.</summary>
        public IReadOnlyList<SettingsRow> Rows(ControlScheme scheme)
        {
            if (_rows.TryGetValue(scheme, out SettingsRow[] rows)) return rows;

            var list = new List<SettingsRow>();
            var heading = new HeadingRow(scheme == ControlScheme.Pad ? PadHeading : KeysHeading);
            heading.Resets(new SchemeReset(this, scheme));
            list.Add(heading);
            foreach (IGrouping<InputAction, RebindSlot> action in Bindings.Slots.Where(slot => slot.Scheme == scheme)
                         .GroupBy(slot => slot.Action))
            {
                // OrderBy is stable: two keys of one way (W and Up for throttle) stay in the asset's order.
                RebindSlot[] slots = action.OrderBy(slot => ControlNames.WayOrder(slot.Part)).ToArray();
                list.Add(new ActionKeysRow(this, action.Key, slots));
            }

            rows = list.ToArray();
            _rows[scheme] = rows;
            return rows;
        }

        /// <summary>Every key of <paramref name="scheme"/> back to its default; the other scheme's keep theirs.</summary>
        public void ResetAll(ControlScheme scheme) => Bindings.ResetAll(scheme);

        /// <summary>The rows of the scheme in hand, made again on each <see cref="SettingsMenu.Rebuild"/>; the section's reset puts that scheme back.</summary>
        public void AddRows(SectionBuilder section) => section.Rows(() => Rows(InHand));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscriptions.Dispose();
            _changed.Dispose();
        }

        /// <summary>The submit key as the device in hand names it; ENTER when the input has no such action.</summary>
        internal string SubmitText()
        {
            try
            {
                string text = Names.Text(_submit);
                return text.Length > 0 ? text : "ENTER";
            }
            catch (ArgumentException)
            {
                return "ENTER";
            }
        }

        private sealed class SchemeReset : IResettable
        {
            private readonly ControlKeys _keys;
            private readonly ControlScheme _scheme;

            public SchemeReset(ControlKeys keys, ControlScheme scheme)
            {
                _keys = keys;
                _scheme = scheme;
            }

            public void Reset() => _keys.ResetAll(_scheme);
        }
    }
}
