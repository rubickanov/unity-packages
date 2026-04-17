using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine.Localization;

namespace Rubickanov.Localization
{
    /// <summary>
    /// Service for managing localization with reactive updates.
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>
        /// Initializes Unity Localization and restores the saved locale (if any).
        /// </summary>
        UniTask InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Current locale with code and display names.
        /// </summary>
        ReadOnlyReactiveProperty<LangLocale> CurrentLocale { get; }

        /// <summary>
        /// Whether the current locale uses right-to-left text direction.
        /// </summary>
        ReadOnlyReactiveProperty<bool> IsRTL { get; }

        /// <summary>
        /// Observable that emits when locale changes.
        /// </summary>
        Observable<Locale> OnLocaleChanged { get; }

        /// <summary>
        /// Gets a localized string.
        /// </summary>
        string GetString(LocalizationKey key);

        /// <summary>
        /// Gets a localized string with format arguments (Smart Strings).
        /// </summary>
        string GetString(LocalizationKey key, params object[] arguments);

        /// <summary>
        /// Creates a reactive localized value that auto-updates on locale change.
        /// </summary>
        LocalizedValue Localize(LocalizationKey key);

        /// <summary>
        /// Creates a reactive localized value with arguments that auto-updates on locale change.
        /// </summary>
        LocalizedValue Localize(LocalizationKey key, params object[] arguments);

        /// <summary>
        /// Changes the current locale by code. Completes only after Unity has applied the locale.
        /// </summary>
        UniTask SetLocaleAsync(string localeCode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Changes the current locale. Completes only after Unity has applied the locale.
        /// </summary>
        UniTask SetLocaleAsync(LangLocale locale, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all available locales. Cached after <see cref="InitializeAsync"/>.
        /// </summary>
        LangLocale[] GetAvailableLocales();
    }
}
