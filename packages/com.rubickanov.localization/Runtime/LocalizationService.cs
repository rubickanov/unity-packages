using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using R3;
using Rubickanov.Storage;
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

        private const string StorageKey = "localization.locale";

        private readonly ILogger<LocalizationService> _logger;
        private readonly ILogger<LocalizedValue> _localizedValueLogger;
        private readonly IStorageService? _storage;
        private readonly Dictionary<LocalizationKey, LocalizedString> _cache = new();
        private readonly ReactiveProperty<LangLocale> _currentLocale;
        private readonly ReactiveProperty<bool> _isRtl;
        private readonly Subject<Locale> _onLocaleChanged;

        private LangLocale[] _cachedAvailableLocales = Array.Empty<LangLocale>();
        private UniTask _pendingSave = UniTask.CompletedTask;
        private bool _disposed;

        public ReadOnlyReactiveProperty<LangLocale> CurrentLocale => _currentLocale;
        public ReadOnlyReactiveProperty<bool> IsRTL => _isRtl;
        public Observable<Locale> OnLocaleChanged => _onLocaleChanged;

        public LocalizationService(
            ILoggerFactory loggerFactory,
            IStorageService? storage = null)
        {
            _logger = loggerFactory.CreateLogger<LocalizationService>();
            _localizedValueLogger = loggerFactory.CreateLogger<LocalizedValue>();
            _storage = storage;

            _currentLocale = new ReactiveProperty<LangLocale>(LangLocale.Empty);
            _isRtl = new ReactiveProperty<bool>(false);
            _onLocaleChanged = new Subject<Locale>();

            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            await LocalizationSettings.InitializationOperation;
            cancellationToken.ThrowIfCancellationRequested();

            _logger.ZLogDebug($"LocalizationService initialized");
            CacheAvailableLocales();
            RestoreSavedLocale();
        }

        private void CacheAvailableLocales()
        {
            var locales = LocalizationSettings.AvailableLocales.Locales;
            _cachedAvailableLocales = new LangLocale[locales.Count];
            for (var i = 0; i < locales.Count; i++)
            {
                _cachedAvailableLocales[i] = new LangLocale(locales[i].Identifier.Code);
            }
        }

        private void RestoreSavedLocale()
        {
            var savedLocaleCode = _storage?.HasKey(StorageKey) == true
                ? _storage.GetString(StorageKey)
                : null;

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
                    if (LocalizationSettings.SelectedLocale != locale)
                    {
                        LocalizationSettings.SelectedLocale = locale;
                        // OnSelectedLocaleChanged will populate _currentLocale / _isRtl.
                    }
                    else
                    {
                        // Already selected — Unity will not fire the event, set manually.
                        _currentLocale.Value = new LangLocale(savedLocaleCode);
                        _isRtl.Value = IsRtlLocale(savedLocaleCode);
                    }
                    _logger.ZLogDebug($"Restored saved locale: {savedLocaleCode}");
                    return;
                }
            }

            var defaultLocale = LocalizationSettings.SelectedLocale;
            var defaultCode = defaultLocale?.Identifier.Code ?? string.Empty;
            _currentLocale.Value = new LangLocale(defaultCode);
            _isRtl.Value = IsRtlLocale(defaultCode);
            _logger.ZLogWarning($"Saved locale '{savedLocaleCode}' not found, clearing.");

            ChainSave(string.Empty);
        }

        private static bool IsRtlLocale(string localeCode)
        {
            if (string.IsNullOrEmpty(localeCode))
                return false;

            var dash = localeCode.IndexOf('-');
            var primaryCode = dash < 0 ? localeCode : localeCode.Substring(0, dash);
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
            return new LocalizedValue(GetOrCreateLocalizedString(key), _onLocaleChanged, ResolveLocalizedString, logger: _localizedValueLogger);
        }

        public LocalizedValue Localize(LocalizationKey key, params object[] arguments)
        {
            return new LocalizedValue(GetOrCreateLocalizedString(key), _onLocaleChanged, ResolveLocalizedString, arguments, _localizedValueLogger);
        }

        public async UniTask SetLocaleAsync(string localeCode, CancellationToken cancellationToken = default)
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
                return;
            }

            if (LocalizationSettings.SelectedLocale == targetLocale)
                return;

            var tcs = new UniTaskCompletionSource();
            IDisposable? subscription = null;
            subscription = _onLocaleChanged
                .Where(localeCode, static (l, code) => l.Identifier.Code == code)
                .Take(1)
                .Subscribe(tcs, static (_, t) => t.TrySetResult());

            using var ctRegistration = cancellationToken.Register(() =>
            {
                subscription?.Dispose();
                tcs.TrySetCanceled(cancellationToken);
            });

            try
            {
                LocalizationSettings.SelectedLocale = targetLocale;
                await tcs.Task;
            }
            finally
            {
                subscription?.Dispose();
            }
        }

        public UniTask SetLocaleAsync(LangLocale locale, CancellationToken cancellationToken = default)
        {
            return SetLocaleAsync(locale.Code, cancellationToken);
        }

        public LangLocale[] GetAvailableLocales() => _cachedAvailableLocales;

        private LocalizedString GetOrCreateLocalizedString(LocalizationKey key)
        {
            if (!key.IsValid)
                throw new ArgumentException("LocalizationKey is not valid (default or empty).", nameof(key));

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

            _logger.ZLogInformation($"Locale changed: {code}, RTL: {isRtl}");

            _currentLocale.Value = new LangLocale(code);
            _isRtl.Value = isRtl;
            _onLocaleChanged.OnNext(locale);

            ChainSave(code);
        }

        private void ChainSave(string code)
        {
            if (_storage == null) return;

            _pendingSave = SaveSerialized(_pendingSave, code);
        }

        private async UniTask SaveSerialized(UniTask previous, string code)
        {
            try
            {
                await previous;
            }
            catch
            {
                // previous save already logged its own error
            }

            try
            {
                await _storage!.SetString(StorageKey, code);
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Failed to save locale '{code}' to storage");
            }
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
