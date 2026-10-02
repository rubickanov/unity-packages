using System;
using Microsoft.Extensions.Logging;
using R3;
using UnityEngine.Localization;
using ZLogger;

namespace Rubickanov.Localization
{
    /// <summary>
    /// Reactive wrapper for a localized string that auto-updates on locale change.
    /// Supports changing the key dynamically via SetKey().
    /// </summary>
    public sealed class LocalizedValue : IDisposable
    {
        private LocalizedString _localizedString;
        private readonly Func<LocalizationKey, LocalizedString>? _resolver;
        private readonly ReactiveProperty<string> _value;
        private readonly IDisposable _subscription;
        private readonly ILogger? _logger;
        private readonly bool _isStatic;
        private object[]? _arguments;
        private bool _disposed;

        /// <summary>
        /// Reactive property containing the current localized string value.
        /// </summary>
        public ReadOnlyReactiveProperty<string> Value => _value;

        /// <summary>
        /// Gets the current localized string value.
        /// </summary>
        public string CurrentValue => _value.CurrentValue;

        internal LocalizedValue(
            LocalizedString localizedString,
            Observable<Locale> onLocaleChanged,
            Func<LocalizationKey, LocalizedString>? resolver = null,
            object[]? arguments = null,
            ILogger? logger = null)
        {
            _localizedString = localizedString;
            _resolver = resolver;
            _arguments = arguments;
            _logger = logger;
            _isStatic = false;
            _value = new ReactiveProperty<string>(GetLocalizedString());

            _subscription = onLocaleChanged.Subscribe(this, static (_, self) => self.UpdateValue());
        }

        /// <summary>
        /// Creates a LocalizedValue with a static value (no subscriptions, no Unity Localization).
        /// Used by NullLocalizationService. SetKey/SetArguments are no-ops in this mode.
        /// </summary>
        internal LocalizedValue(string staticValue)
        {
            _localizedString = default!;
            _resolver = null;
            _arguments = null;
            _logger = null;
            _isStatic = true;
            _value = new ReactiveProperty<string>(staticValue);
            _subscription = Disposable.Empty;
        }

        /// <summary>
        /// Changes the localization key and updates the value.
        /// Uses the service cache when available. No-op in static mode.
        /// </summary>
        public void SetKey(LocalizationKey key)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LocalizedValue));

            if (_isStatic) return;

            _localizedString = _resolver != null
                ? _resolver(key)
                : new LocalizedString(key.Table, key.Key);
            UpdateValue();
        }

        /// <summary>
        /// Updates arguments for Smart String formatting and refreshes the value.
        /// No-op in static mode.
        /// </summary>
        public void SetArguments(params object[] arguments)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LocalizedValue));

            if (_isStatic) return;

            _arguments = arguments;
            UpdateValue();
        }

        private void UpdateValue()
        {
            if (_disposed) return;
            _value.Value = GetLocalizedString();
        }

        private string GetLocalizedString()
        {
            if (_localizedString.IsEmpty)
                return string.Empty;

            var entry = _localizedString.GetLocalizedString();

            if (_arguments is { Length: > 0 })
            {
                try
                {
                    return string.Format(entry, _arguments);
                }
                catch (FormatException ex)
                {
                    _logger?.ZLogWarning($"Format error in LocalizedValue for '{entry}': {ex.Message}");
                    return entry;
                }
            }

            return entry;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _subscription.Dispose();
            _value.Dispose();
        }
    }
}
