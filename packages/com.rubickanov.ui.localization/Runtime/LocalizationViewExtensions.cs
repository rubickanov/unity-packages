using Rubickanov.Localization;

namespace Rubickanov.UI.Localization
{
    /// <summary>
    /// Backend-agnostic localization helpers for ViewModels.
    /// </summary>
    public static class LocalizationViewExtensions
    {
        /// <summary>
        /// Creates a <see cref="LocalizedValue"/> bound to a key and tracks its disposal in the ViewModel.
        /// </summary>
        public static LocalizedValue CreateLocalized(
            this ViewModelBase vm, ILocalizationService loc, LocalizationKey key)
        {
            var value = loc.Localize(key);
            vm.TrackDisposable(value);
            return value;
        }
    }
}
