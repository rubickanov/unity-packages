using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Rubickanov.Input
{
    /// <summary>
    /// The action maps a block takes: every map (<see cref="All"/>), or the maps a game names. Declared once by the game
    /// as data, <c>static readonly InputScope Gameplay = InputScope.Of("Player")</c>, and passed with each block.
    /// </summary>
    public sealed class InputScope
    {
        private readonly string[]? _maps;

        private InputScope(string[]? maps) => _maps = maps;

        /// <summary>Every map of the input: a console that types, a key being rebound.</summary>
        public static InputScope All { get; } = new(null);

        /// <summary>The maps named <paramref name="maps"/>, case aside; every map when none is named.</summary>
        public static InputScope Of(params string[] maps) =>
            maps == null || maps.Length == 0 ? All : new InputScope((string[])maps.Clone());

        /// <summary>The maps it names; null for every map.</summary>
        public IReadOnlyList<string>? Maps => _maps;

        /// <summary>Whether a block of this scope takes <paramref name="map"/>.</summary>
        public bool Covers(InputActionMap map)
        {
            if (_maps == null)
            {
                return true;
            }

            for (int i = 0; i < _maps.Length; i++)
            {
                if (string.Equals(_maps[i], map.name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString() => _maps == null ? "All" : string.Join("+", _maps);
    }

    /// <summary>
    /// Takes the player's actions away while something else needs the devices: a menu takes the gameplay map, a console
    /// that types every map. Every map of the input is on from construction until a reason blocks it, and off while any
    /// reason does; each reason blocks under its own key, so lifting one does not lift another. A game that switches
    /// maps by mode (on foot, in a ship) blocks the other mode's map under a reason of its own, so the blocker stays the
    /// one place maps are switched. One per input.
    /// </summary>
    public sealed class InputBlocker : IDisposable
    {
        private readonly List<InputActionMap> _maps;
        private readonly Dictionary<object, InputScope> _reasons = new();
        private bool _disposed;

        public InputBlocker(IInputActionCollection2 input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            _maps = InputMaps.Of(input);
            Apply();
        }

        /// <summary>Any reason blocks anything now.</summary>
        public bool IsBlocked => _reasons.Count > 0;

        /// <summary>Raised when <see cref="IsBlocked"/> flips, with its new value.</summary>
        public event Action<bool>? Changed;

        /// <summary>Whether a reason blocks <paramref name="map"/> now.</summary>
        public bool IsMapBlocked(InputActionMap map)
        {
            foreach (KeyValuePair<object, InputScope> reason in _reasons)
            {
                if (reason.Value.Covers(map))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Blocks <paramref name="scope"/>'s maps (every map when null) under <paramref name="reason"/>, or lifts that
        /// reason's block whatever its scope. Blocking again under the same reason replaces its scope.
        /// </summary>
        public void Set(object reason, bool blocked, InputScope? scope = null)
        {
            if (reason == null)
            {
                throw new ArgumentNullException(nameof(reason));
            }

            bool wasBlocked = IsBlocked;
            if (blocked)
            {
                _reasons[reason] = scope ?? InputScope.All;
            }
            else
            {
                _reasons.Remove(reason);
            }

            Apply();
            if (IsBlocked != wasBlocked)
            {
                Changed?.Invoke(IsBlocked);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _reasons.Clear();
            foreach (InputActionMap map in _maps)
            {
                map.Disable();
            }
        }

        private void Apply()
        {
            if (_disposed)
            {
                return;
            }

            foreach (InputActionMap map in _maps)
            {
                if (IsMapBlocked(map))
                {
                    map.Disable();
                }
                else
                {
                    map.Enable();
                }
            }
        }
    }
}
