using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>Manages command aliases. Aliases map a short name to a full command string.</summary>
    public class AliasRegistry
    {
        private static AliasRegistry? _instance;
        public static AliasRegistry Instance => _instance ??= new AliasRegistry();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => _instance = null;

        private readonly Dictionary<string, string> _aliases = new();
        private string[]? _sortedNames;

        /// <summary>All registered aliases.</summary>
        public IReadOnlyDictionary<string, string> Aliases => _aliases;

        /// <summary>Alias names in ordinal order, rebuilt only after a change. For autocomplete and listings.</summary>
        public IReadOnlyList<string> SortedNames
        {
            get
            {
                if (_sortedNames == null)
                {
                    _sortedNames = new string[_aliases.Count];
                    _aliases.Keys.CopyTo(_sortedNames, 0);
                    Array.Sort(_sortedNames, StringComparer.Ordinal);
                }

                return _sortedNames;
            }
        }

        private AliasRegistry() => ConsoleConfig.ReadAliases(_aliases);

        /// <summary>Returns true if the name is a registered alias and outputs the command it maps to.</summary>
        public bool TryResolve(string name, [NotNullWhen(true)] out string? command)
        {
            return _aliases.TryGetValue(name.ToLowerInvariant(), out command);
        }

        /// <summary>
        /// Whether <paramref name="name"/> can be typed as a command: not empty, and none of the characters the command
        /// line gives a meaning to (space, quote, <c>;</c>, <c>$</c>).
        /// </summary>
        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsWhiteSpace(c) || c == '"' || c == ';' || c == '$') return false;
            }

            return true;
        }

        /// <summary>Creates or overwrites an alias. Throws on a name <see cref="IsValidName"/> rejects.</summary>
        public void Set(string name, string command)
        {
            if (!IsValidName(name))
                throw new ArgumentException($"'{name}' cannot be an alias name.", nameof(name));
            _aliases[name.ToLowerInvariant()] = command;
            _sortedNames = null;
            Save();
        }

        /// <summary>Removes an alias. Returns true if it existed.</summary>
        public bool Remove(string name)
        {
            var removed = _aliases.Remove(name.ToLowerInvariant());
            if (removed)
            {
                _sortedNames = null;
                Save();
            }
            return removed;
        }

        /// <summary>Removes all aliases.</summary>
        public void Clear()
        {
            _aliases.Clear();
            _sortedNames = null;
            Save();
        }

        /// <summary>
        /// The command an alias call runs. <c>$1</c>..<c>$9</c> in the alias are replaced by the call's arguments and
        /// <c>$*</c> by all of them, an argument that was not given becoming empty; an alias without any of these gets
        /// the arguments appended. Arguments are taken as typed, so a quoted one stays a single argument.
        /// </summary>
        internal static string Expand(string template, CommandLine call)
        {
            StringBuilder? sb = null;
            var copied = 0;
            for (int i = 0; i < template.Length - 1; i++)
            {
                if (template[i] != '$') continue;
                var next = template[i + 1];

                string value;
                if (next == '*')
                    value = call.RawTail(1);
                else if (next >= '1' && next <= '9')
                    value = next - '0' < call.Count ? call.RawToken(next - '0') : "";
                else
                    continue;

                sb ??= new StringBuilder();
                sb.Append(template, copied, i - copied).Append(value);
                copied = i + 2;
                i++;
            }

            if (sb != null)
                return sb.Append(template, copied, template.Length - copied).ToString();

            var tail = call.RawTail(1);
            return tail.Length > 0 ? template + " " + tail : template;
        }

        private void Save() => ConsoleConfig.Write(this, BindingRegistry.Instance);
    }
}
