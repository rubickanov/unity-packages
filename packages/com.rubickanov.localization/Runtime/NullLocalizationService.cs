using System;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine.Localization;

namespace Rubickanov.Localization
{
    /// <summary>
    /// No-op localization service for server and headless builds.
    /// All methods return empty strings, empty arrays, or completed tasks.
    /// </summary>
    public class NullLocalizationService : ILocalizationService
    {
        private readonly ReactiveProperty<LangLocale> _currentLocale = new(LangLocale.Empty);
        private readonly ReactiveProperty<bool> _isRtl = new(false);

        /// <inheritdoc />
        public ReadOnlyReactiveProperty<LangLocale> CurrentLocale => _currentLocale;

        /// <inheritdoc />
        public ReadOnlyReactiveProperty<bool> IsRTL => _isRtl;

        /// <inheritdoc />
        public Observable<Locale> OnLocaleChanged => Observable.Empty<Locale>();

        /// <inheritdoc />
        public UniTask InitializeAsync() => UniTask.CompletedTask;

        /// <inheritdoc />
        public string GetString(LocalizationKey key) => string.Empty;

        /// <inheritdoc />
        public string GetString(LocalizationKey key, params object[] arguments) => string.Empty;

        /// <inheritdoc />
        public LocalizedValue Localize(LocalizationKey key) => new(string.Empty);

        /// <inheritdoc />
        public LocalizedValue Localize(LocalizationKey key, params object[] arguments) => new(string.Empty);

        /// <inheritdoc />
        public UniTask SetLocaleAsync(string localeCode) => UniTask.CompletedTask;

        /// <inheritdoc />
        public UniTask SetLocaleAsync(LangLocale locale) => UniTask.CompletedTask;

        /// <inheritdoc />
        public LangLocale[] GetAvailableLocales() => Array.Empty<LangLocale>();
    }
}
