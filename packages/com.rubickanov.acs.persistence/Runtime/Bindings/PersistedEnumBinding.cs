using System;
using R3;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Persistence
{
    /// <summary>
    /// <see cref="ReactiveProperty{T}"/> binding for enum fields. Encoding mode is
    /// picked explicitly via <see cref="PersistedEnumAttribute"/>; the scanner rejects
    /// enum fields without it. ReadValue returns a string or long depending on mode;
    /// WriteValue accepts the same shape and also tolerates the boxed enum instance
    /// (e.g. a serializer that round-trips the original TEnum without coercing it).
    /// </summary>
    internal sealed class PersistedEnumBinding<TEnum> : PersistedFieldBinding
        where TEnum : struct, Enum
    {
        private readonly ReactiveProperty<TEnum> _reactive;
        private readonly PersistedEnumMode _mode;

        public PersistedEnumBinding(ReactiveProperty<TEnum> reactive, PersistedEnumMode mode)
        {
            Debug.Assert(reactive != null, "PersistedEnumBinding: reactive is null — factory must reject uninitialized [PersistedState] fields.");
            _reactive = reactive;
            _mode = mode;
        }

        public override object ReadValue()
        {
            return _mode switch
            {
                PersistedEnumMode.ByName => _reactive.Value.ToString(),
                PersistedEnumMode.ByValue => Convert.ToInt64(_reactive.Value),
                _ => throw new InvalidOperationException($"Unknown PersistedEnumMode: {_mode}"),
            };
        }

        public override void WriteValue(object value)
        {
            if (value is TEnum direct)
            {
                _reactive.Value = direct;
                return;
            }

            switch (_mode)
            {
                case PersistedEnumMode.ByName:
                {
                    if (value is not string name)
                        throw new InvalidCastException(
                            $"[acs.persistence] PersistedEnumBinding<{typeof(TEnum).Name}> ByName expected string, got " +
                            $"{value?.GetType().Name ?? "null"}.");

                    if (!Enum.TryParse(typeof(TEnum), name, ignoreCase: false, out var parsed))
                    {
                        Debug.LogWarning(
                            $"[acs.persistence] Enum '{typeof(TEnum).FullName}' has no member '{name}' — snapshot predates " +
                            $"a rename or a new member. Field value kept at {_reactive.Value}. Register an IAspectMigrator " +
                            $"to remap legacy names.");
                        return;
                    }

                    _reactive.Value = (TEnum)parsed;
                    return;
                }

                case PersistedEnumMode.ByValue:
                {
                    long numeric;
                    try
                    {
                        numeric = Convert.ToInt64(value);
                    }
                    catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException)
                    {
                        throw new InvalidCastException(
                            $"[acs.persistence] PersistedEnumBinding<{typeof(TEnum).Name}> ByValue could not convert " +
                            $"{value?.GetType().Name ?? "null"} '{value}' to the underlying integer: {ex.Message}");
                    }

                    var asEnum = (TEnum)Enum.ToObject(typeof(TEnum), numeric);
                    if (!Enum.IsDefined(typeof(TEnum), asEnum))
                    {
                        Debug.LogWarning(
                            $"[acs.persistence] Enum '{typeof(TEnum).FullName}' value {numeric} is not defined — snapshot predates " +
                            $"a reorder or insert. Field value kept at {_reactive.Value}. Switch to ByName or register a migrator.");
                        return;
                    }

                    _reactive.Value = asEnum;
                    return;
                }

                default:
                    throw new InvalidOperationException($"Unknown PersistedEnumMode: {_mode}");
            }
        }
    }
}
