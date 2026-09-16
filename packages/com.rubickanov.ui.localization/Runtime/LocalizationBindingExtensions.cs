using System;
using Rubickanov.Localization;
using Rubickanov.UI.UIToolkit;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Localization.UIToolkit
{
    /// <summary>
    /// One-line localization binding helpers for UIToolkit views.
    /// </summary>
    /// <remarks>
    /// Subscriptions are registered through <see cref="UIToolkitView{TVM}.BindObservable{T}"/> and automatically
    /// disposed when the view is unbound. Requires <see cref="ILocalizationService.InitializeAsync"/> to have
    /// completed before any binding is created.
    /// </remarks>
    public static class UIToolkitLocalizationExtensions
    {
        /// <summary>
        /// Binds <paramref name="label"/>.text to the localized string for <paramref name="key"/>, refreshing on
        /// <see cref="ILocalizationService.OnLocaleChanged"/>. Resolves the service via <c>view.GetService</c>.
        /// </summary>
        public static void BindLocalized<TVM>(
            this UIToolkitView<TVM> view, Label label, LocalizationKey key)
            where TVM : ViewModelBase
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            BindLocalized(view, view.GetService<ILocalizationService>(), label, key);
        }

        /// <summary>
        /// Explicit-service overload of <see cref="BindLocalized{TVM}(UIToolkitView{TVM}, Label, LocalizationKey)"/>.
        /// Use when resolving <see cref="ILocalizationService"/> without the DI container (tests, presenters).
        /// </summary>
        public static void BindLocalized<TVM>(
            this UIToolkitView<TVM> view, ILocalizationService loc, Label label, LocalizationKey key)
            where TVM : ViewModelBase
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (loc == null) throw new ArgumentNullException(nameof(loc));
            if (label == null) throw new ArgumentNullException(nameof(label));
            if (!key.IsValid)
                throw new ArgumentException("LocalizationKey must have non-empty Table and Key.", nameof(key));

            SetTextIfChanged(label, loc.GetString(key));
            view.BindObservable(loc.OnLocaleChanged, _ => SetTextIfChanged(label, loc.GetString(key)));
        }

        /// <summary>Binds <paramref name="button"/>.text to <paramref name="key"/>. See <see cref="BindLocalized{TVM}(UIToolkitView{TVM}, Label, LocalizationKey)"/>.</summary>
        public static void BindLocalized<TVM>(
            this UIToolkitView<TVM> view, Button button, LocalizationKey key)
            where TVM : ViewModelBase
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            BindLocalized(view, view.GetService<ILocalizationService>(), button, key);
        }

        /// <summary>Explicit-service overload for buttons.</summary>
        public static void BindLocalized<TVM>(
            this UIToolkitView<TVM> view, ILocalizationService loc, Button button, LocalizationKey key)
            where TVM : ViewModelBase
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (loc == null) throw new ArgumentNullException(nameof(loc));
            if (button == null) throw new ArgumentNullException(nameof(button));
            if (!key.IsValid)
                throw new ArgumentException("LocalizationKey must have non-empty Table and Key.", nameof(key));

            SetTextIfChanged(button, loc.GetString(key));
            view.BindObservable(loc.OnLocaleChanged, _ => SetTextIfChanged(button, loc.GetString(key)));
        }

        /// <summary>Binds <paramref name="label"/>.text via a factory that computes the string from the service.</summary>
        public static void BindLocalized<TVM>(
            this UIToolkitView<TVM> view, Label label, Func<ILocalizationService, string> textFactory)
            where TVM : ViewModelBase
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            BindLocalized(view, view.GetService<ILocalizationService>(), label, textFactory);
        }

        /// <summary>Explicit-service overload of the factory variant.</summary>
        public static void BindLocalized<TVM>(
            this UIToolkitView<TVM> view, ILocalizationService loc, Label label, Func<ILocalizationService, string> textFactory)
            where TVM : ViewModelBase
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (loc == null) throw new ArgumentNullException(nameof(loc));
            if (label == null) throw new ArgumentNullException(nameof(label));
            if (textFactory == null) throw new ArgumentNullException(nameof(textFactory));

            SetTextIfChanged(label, textFactory(loc));
            view.BindObservable(loc.OnLocaleChanged, _ => SetTextIfChanged(label, textFactory(loc)));
        }

        /// <summary>Binds <paramref name="button"/>.text via a factory that computes the string from the service.</summary>
        public static void BindLocalized<TVM>(
            this UIToolkitView<TVM> view, Button button, Func<ILocalizationService, string> textFactory)
            where TVM : ViewModelBase
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            BindLocalized(view, view.GetService<ILocalizationService>(), button, textFactory);
        }

        /// <summary>Explicit-service overload of the factory variant for buttons.</summary>
        public static void BindLocalized<TVM>(
            this UIToolkitView<TVM> view, ILocalizationService loc, Button button, Func<ILocalizationService, string> textFactory)
            where TVM : ViewModelBase
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (loc == null) throw new ArgumentNullException(nameof(loc));
            if (button == null) throw new ArgumentNullException(nameof(button));
            if (textFactory == null) throw new ArgumentNullException(nameof(textFactory));

            SetTextIfChanged(button, textFactory(loc));
            view.BindObservable(loc.OnLocaleChanged, _ => SetTextIfChanged(button, textFactory(loc)));
        }

        /// <summary>
        /// Parameterized binding: re-evaluates <paramref name="argsFactory"/> on every locale change and formats
        /// via <see cref="ILocalizationService.GetString(LocalizationKey, object[])"/>.
        /// </summary>
        public static void BindLocalized<TVM>(
            this UIToolkitView<TVM> view, Label label, LocalizationKey key, Func<object[]> argsFactory)
            where TVM : ViewModelBase
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            BindLocalized(view, view.GetService<ILocalizationService>(), label, key, argsFactory);
        }

        /// <summary>Explicit-service overload of the parameterized binding.</summary>
        public static void BindLocalized<TVM>(
            this UIToolkitView<TVM> view, ILocalizationService loc, Label label, LocalizationKey key, Func<object[]> argsFactory)
            where TVM : ViewModelBase
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (loc == null) throw new ArgumentNullException(nameof(loc));
            if (label == null) throw new ArgumentNullException(nameof(label));
            if (argsFactory == null) throw new ArgumentNullException(nameof(argsFactory));
            if (!key.IsValid)
                throw new ArgumentException("LocalizationKey must have non-empty Table and Key.", nameof(key));

            SetTextIfChanged(label, loc.GetString(key, argsFactory()));
            view.BindObservable(loc.OnLocaleChanged, _ => SetTextIfChanged(label, loc.GetString(key, argsFactory())));
        }

        /// <summary>
        /// Toggles <paramref name="element"/>.style.flexDirection between <c>RowReverse</c> (RTL) and <c>Row</c> (LTR)
        /// reactively on <see cref="ILocalizationService.IsRTL"/>.
        /// </summary>
        public static void BindIsRTL<TVM>(
            this UIToolkitView<TVM> view, VisualElement element)
            where TVM : ViewModelBase
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            BindIsRTL(view, view.GetService<ILocalizationService>(), element);
        }

        /// <summary>Explicit-service overload of <see cref="BindIsRTL{TVM}(UIToolkitView{TVM}, VisualElement)"/>.</summary>
        public static void BindIsRTL<TVM>(
            this UIToolkitView<TVM> view, ILocalizationService loc, VisualElement element)
            where TVM : ViewModelBase
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (loc == null) throw new ArgumentNullException(nameof(loc));
            if (element == null) throw new ArgumentNullException(nameof(element));

            element.style.flexDirection = loc.IsRTL.CurrentValue ? FlexDirection.RowReverse : FlexDirection.Row;
            view.BindObservable(loc.IsRTL, rtl =>
                element.style.flexDirection = rtl ? FlexDirection.RowReverse : FlexDirection.Row);
        }

        private static void SetTextIfChanged(Label label, string next)
        {
            if (label.text != next) label.text = next;
        }

        private static void SetTextIfChanged(Button button, string next)
        {
            if (button.text != next) button.text = next;
        }
    }
}
