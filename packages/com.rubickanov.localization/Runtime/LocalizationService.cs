using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using R3;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using ZLogger;

namespace Rubickanov.Localization
{
    /// <summary>
    /// Localization service implementation with reactive updates and caching.
    /// Persists selected locale between sessions via caller-provided delegates.
    /// </summary>
    public sealed class LocalizationService : ILocalizationService, IDisposable
    {
        private static readonly HashSet<string> RtlLocaleCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            "ar", "he", "fa", "ur", "yi", "ps", "sd", "ug"
        };

        private readonly ILogger<LocalizationService> _logger;
        private readonly Func<string?> _loadLocale;
        private readonly Action<string> _saveLocale;
        private readonly Dictionary<LocalizationKey, LocalizedString> _cache = new();
        private readonly ReactiveProperty<LangLocale> _currentLocale;
        private readonly ReactiveProperty<bool> _isRtl;
        private readonly Subject<Locale> _onLocaleChanged;
        private bool _disposed;

        public ReadOnlyReactiveProperty<LangLocale> CurrentLocale => _currentLocale;
        public ReadOnlyReactiveProperty<bool> IsRTL => _isRtl;
        public Observable<Locale> OnLocaleChanged => _onLocaleChanged;

        /// <summary>
        /// Creates a new localization service.
        /// </summary>
        /// <param name="loggerFactory">Logger factory for diagnostic output.</param>
        /// <param name="loadLocale">Delegate that returns the saved locale code, or null if none.</param>
        /// <param name="saveLocale">Delegate that persists the selected locale code.</param>
        public LocalizationService(
            ILoggerFactory loggerFactory,
            Func<string?> loadLocale,
            Action<string> saveLocale)
        {
            _logger = loggerFactory.CreateLogger<LocalizationService>();
            _loadLocale = loadLocale;
            _saveLocale = saveLocale;

            _currentLocale = new ReactiveProperty<LangLocale>(LangLocale.Empty);
            _isRtl = new ReactiveProperty<bool>(false);
            _onLocaleChanged = new Subject<Locale>();

            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        public async UniTask InitializeAsync()
        {
            await LocalizationSettings.InitializationOperation;
            _logger.ZLogDebug($"LocalizationService initialized");
            RestoreSavedLocale();
        }

        private void RestoreSavedLocale()
        {
            var savedLocaleCode = _loadLocale();

            if (string.IsNullOrEmpty(savedLocaleCode))
            {
                var currentLocale = LocalizationSettings.SelectedLocale;
                var code = currentLocale?.Identifier.Code ?? string.Empty;
                _currentLocale.Value = new LangLocale(code);
                _isRtl.Value = IsRtlLocale(code);
                return;
            }

            var locales = LocalizationSettings.AvailableLocales.Locales;
            foreach (var locale in locales)
            {
                if (locale.Identifier.Code == savedLocaleCode)
                {
                    LocalizationSettings.SelectedLocale = locale;
                    _currentLocale.Value = new LangLocale(savedLocaleCode);
                    _isRtl.Value = IsRtlLocale(savedLocaleCode);
                    _logger.ZLogDebug($"Restored saved locale: {savedLocaleCode}");
                    return;
                }
            }

            var defaultLocale = LocalizationSettings.SelectedLocale;
            var defaultCode = defaultLocale?.Identifier.Code ?? string.Empty;
            _currentLocale.Value = new LangLocale(defaultCode);
            _isRtl.Value = IsRtlLocale(defaultCode);
            _logger.ZLogWarning($"Saved locale '{savedLocaleCode}' not found, using default");
        }

        private static bool IsRtlLocale(string localeCode)
        {
            if (string.IsNullOrEmpty(localeCode))
                return false;

            var primaryCode = localeCode.Split('-')[0];
            return RtlLocaleCodes.Contains(primaryCode);
        }

        public string GetString(LocalizationKey key)
        {
            var localizedString = GetOrCreateLocalizedString(key);
            return localizedString.GetLocalizedString();
        }

        public string GetString(LocalizationKey key, params object[] arguments)
        {
            var localizedString = GetOrCreateLocalizedString(key);
            var value = localizedString.GetLocalizedString();

            if (arguments.Length > 0)
            {
                try
                {
                    return string.Format(value, arguments);
                }
                catch (FormatException ex)
                {
                    _logger.ZLogWarning($"Format error for {key}: {ex.Message}");
                    return value;
                }
            }

            return value;
        }

        public LocalizedValue Localize(LocalizationKey key)
        {
            return new LocalizedValue(GetOrCreateLocalizedString(key), _onLocaleChanged, ResolveLocalizedString);
        }

        public LocalizedValue Localize(LocalizationKey key, params object[] arguments)
        {
            return new LocalizedValue(GetOrCreateLocalizedString(key), _onLocaleChanged, ResolveLocalizedString, arguments);
        }

        public UniTask SetLocaleAsync(string localeCode)
        {
            _logger.ZLogDebug($"Setting locale to: {localeCode}");

            var locales = LocalizationSettings.AvailableLocales.Locales;
            Locale? targetLocale = null;

            foreach (var locale in locales)
            {
                if (locale.Identifier.Code == localeCode)
                {
                    targetLocale = locale;
                    break;
                }
            }

            if (targetLocale == null)
            {
                _logger.ZLogWarning($"Locale not found: {localeCode}");
                return UniTask.CompletedTask;
            }

            LocalizationSettings.SelectedLocale = targetLocale;

            _logger.ZLogInformation($"Locale changed to: {localeCode}");
            return UniTask.CompletedTask;
        }

        public UniTask SetLocaleAsync(LangLocale locale)
        {
            return SetLocaleAsync(locale.Code);
        }

        public LangLocale[] GetAvailableLocales()
        {
            var locales = LocalizationSettings.AvailableLocales.Locales;
            var result = new LangLocale[locales.Count];

            for (var i = 0; i < locales.Count; i++)
            {
                var code = locales[i].Identifier.Code;
                result[i] = new LangLocale(code);
            }

            return result;
        }

        private LocalizedString GetOrCreateLocalizedString(LocalizationKey key)
        {
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            var localizedString = new LocalizedString(key.Table, key.Key);
            _cache[key] = localizedString;
            return localizedString;
        }

        private LocalizedString ResolveLocalizedString(LocalizationKey key)
        {
            return GetOrCreateLocalizedString(key);
        }

        private void OnSelectedLocaleChanged(Locale locale)
        {
            var code = locale.Identifier.Code;
            var isRtl = IsRtlLocale(code);

            _logger.ZLogDebug($"Locale changed event: {code}, RTL: {isRtl}");

            _currentLocale.Value = new LangLocale(code);
            _isRtl.Value = isRtl;
            _onLocaleChanged.OnNext(locale);

            _saveLocale(code);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
            _currentLocale.Dispose();
            _isRtl.Dispose();
            _onLocaleChanged.Dispose();
            _cache.Clear();

            _logger.ZLogDebug($"LocalizationService disposed");
        }
    }
}
