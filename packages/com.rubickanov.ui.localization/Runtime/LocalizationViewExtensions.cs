using System;
using Rubickanov.Localization;

namespace Rubickanov.UI.Localization
{
    /// <summary>
    /// Backend-agnostic localization helpers for ViewModels.
    /// </summary>
    /// <remarks>
    /// Requires <see cref="ILocalizationService.InitializeAsync"/> to have completed before creating
    /// <see cref="LocalizedValue"/>s — unlocalized <c>GetString</c> calls during init return fallbacks.
    /// </remarks>
    public static class LocalizationViewExtensions
    {
        /// <summary>
        /// Creates a <see cref="LocalizedValue"/> bound to <paramref name="key"/> and tracks its disposal
        /// in <paramref name="vm"/>. The value unsubscribes from locale changes when the ViewModel is unbound.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="vm"/> or <paramref name="loc"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is not valid (i.e. <c>default(LocalizationKey)</c>).</exception>
        public static LocalizedValue CreateLocalized(
            this ViewModelBase vm, ILocalizationService loc, LocalizationKey key)
        {
            if (vm == null) throw new ArgumentNullException(nameof(vm));
            if (loc == null) throw new ArgumentNullException(nameof(loc));
            if (!key.IsValid)
                throw new ArgumentException("LocalizationKey must have non-empty Table and Key.", nameof(key));

            var value = loc.Localize(key);
            vm.TrackDisposable(value);
            return value;
        }
    }
}
