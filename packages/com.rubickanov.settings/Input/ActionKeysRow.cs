using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Rubickanov.Input;
using UnityEngine.InputSystem;

namespace Rubickanov.Settings.Input
{
    /// <summary>
    /// One action's keys of one scheme (Move: W A S D; Jump: SPACE). Enter listens for the picked key's new one: a free
    /// key is taken, a key another action has asks to swap, Esc or Start backs out, a key the slot never takes is denied.
    /// Its id and label key is <c>keys.Map/Action</c>.
    /// </summary>
    public sealed class ActionKeysRow : KeysRow
    {
        private readonly ControlKeys _keys;
        private readonly RebindSlot[] _slots;

        internal ActionKeysRow(ControlKeys keys, InputAction action, RebindSlot[] slots)
            : base(ControlKeys.LabelKey(action))
        {
            _keys = keys;
            _slots = slots;
            Action = action;
        }

        public InputAction Action { get; }

        /// <summary>The keys in the order shown.</summary>
        public IReadOnlyList<RebindSlot> Slots => _slots;

        public ControlScheme Scheme => _slots.Length > 0 ? _slots[0].Scheme : ControlScheme.Keys;

        public override int Count => _slots.Length;

        protected override string ListenKey => Scheme == ControlScheme.Pad ? "settings.listen.pad" : "settings.listen";

        /// <summary>The key's name with its device and family, for a glyph drawer.</summary>
        public KeyName Name(int key) => _keys.Names.Name(_keys.Bindings.Current(_slots[key]));

        public override string KeyText(int key) => Name(key).Text;

        public override bool IsDefault(int key) => _keys.Bindings.IsDefault(_slots[key]);

        protected override string? About(ISettingsText text)
        {
            if (text.TryGet(Id + ".about", out string own)) return own;
            string line = Count > 1 ? "settings.keys.about.ways"
                : Scheme == ControlScheme.Pad ? "settings.keys.about.pad" : "settings.keys.about";
            return string.Format(text.Get(line), _keys.SubmitText());
        }

        protected override async UniTask ListenAsync(int key, ISettingsMenu menu, CancellationToken ct)
        {
            RebindResult result = await _keys.Bindings.ListenAsync(_slots[key], ct);
            switch (result.Kind)
            {
                case RebindKind.Taken when result.Other != null && result.Path != null:
                    menu.Cue(SettingsCue.Denied);
                    string taken = _keys.Names.Name(result.Path).Text;
                    string other = menu.Text.Get(ControlKeys.LabelKey(result.Other.Action));
                    menu.Ask(new SettingsQuestion("settings.swap.question", "settings.yes", "settings.no", yes =>
                    {
                        if (yes) _keys.Bindings.Swap(result);
                    }, args: new object[] { taken, other }));
                    break;
                case RebindKind.Refused:
                case RebindKind.Taken:
                    menu.Cue(SettingsCue.Denied);
                    break;
                case RebindKind.Cancelled:
                    menu.Cue(SettingsCue.Back);
                    break;
                default:
                    menu.Cue(SettingsCue.Select);
                    break;
            }
        }
    }
}
