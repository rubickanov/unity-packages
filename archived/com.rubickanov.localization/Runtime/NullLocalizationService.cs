using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine.Localization;

namespace Rubickanov.Localization
{
    /// <summary>
    /// No-op localization service for server and headless builds.
    /// All methods return empty strings, empty arrays, or completed tasks.
    /// </summary>
    public sealed class NullLocalizationService : ILocalizationService, IDisposable
    {
        private static readonly Observable<Locale> EmptyObservable = Observable.Empty<Locale>();

        private readonly ReactiveProperty<LangLocale> _currentLocale = new(LangLocale.Empty);
        private readonly ReactiveProperty<bool> _isRtl = new(false);
        private bool _disposed;

        /// <inheritdoc />
        public ReadOnlyReactiveProperty<LangLocale> CurrentLocale => _currentLocale;

        /// <inheritdoc />
        public ReadOnlyReactiveProperty<bool> IsRTL => _isRtl;

        /// <inheritdoc />
        public Observable<Locale> OnLocaleChanged => EmptyObservable;

        /// <inheritdoc />
        public UniTask InitializeAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;

        /// <inheritdoc />
        public string GetString(LocalizationKey key) => string.Empty;

        /// <inheritdoc />
        public string GetString(LocalizationKey key, params object[] arguments) => string.Empty;

        /// <inheritdoc />
        public LocalizedValue Localize(LocalizationKey key) => new(string.Empty);

        /// <inheritdoc />
        public LocalizedValue Localize(LocalizationKey key, params object[] arguments) => new(string.Empty);

        /// <inheritdoc />
        public UniTask SetLocaleAsync(string localeCode, CancellationToken cancellationToken = default) => UniTask.CompletedTask;

        /// <inheritdoc />
        public UniTask SetLocaleAsync(LangLocale locale, CancellationToken cancellationToken = default) => UniTask.CompletedTask;

        /// <inheritdoc />
        public LangLocale[] GetAvailableLocales() => Array.Empty<LangLocale>();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _currentLocale.Dispose();
            _isRtl.Dispose();
        }
    }
}
