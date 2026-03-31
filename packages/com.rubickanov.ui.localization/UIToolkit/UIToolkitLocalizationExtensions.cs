using System;
using Rubickanov.Localization;
using Rubickanov.UI.UIToolkit;
using UnityEngine.UIElements;

namespace Rubickanov.UI.Localization.UIToolkit
{
    /// <summary>
    /// One-line localization binding helpers for UIToolkit views.
    /// </summary>
    public static class UIToolkitLocalizationExtensions
    {
        public static void BindLocalized<TVM>(
            this UIToolkitView<TVM> view, Label label, LocalizationKey key)
            where TVM : ViewModelBase
        {
            var loc = view.GetService<ILocalizationService>();
            label.text = loc.GetString(key);
            view.BindObservable(loc.OnLocaleChanged, _ => label.text = loc.GetString(key));
        }

        public static void BindLocalized<TVM>(
            this UIToolkitView<TVM> view, Button button, LocalizationKey key)
            where TVM : ViewModelBase
        {
            var loc = view.GetService<ILocalizationService>();
            button.text = loc.GetString(key);
            view.BindObservable(loc.OnLocaleChanged, _ => button.text = loc.GetString(key));
        }

        public static void BindLocalized<TVM>(
            this UIToolkitView<TVM> view, Label label, Func<ILocalizationService, string> textFactory)
            where TVM : ViewModelBase
        {
            var loc = view.GetService<ILocalizationService>();
            label.text = textFactory(loc);
            view.BindObservable(loc.OnLocaleChanged, _ => label.text = textFactory(loc));
        }

        public static void BindLocalized<TVM>(
            this UIToolkitView<TVM> view, Button button, Func<ILocalizationService, string> textFactory)
            where TVM : ViewModelBase
        {
            var loc = view.GetService<ILocalizationService>();
            button.text = textFactory(loc);
            view.BindObservable(loc.OnLocaleChanged, _ => button.text = textFactory(loc));
        }
    }
}
